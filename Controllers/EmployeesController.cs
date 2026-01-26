using System.Text;
using EasyRecordWorkingApi.Contracts;
using EasyRecordWorkingApi.Data;
using EasyRecordWorkingApi.Dtos;
using EasyRecordWorkingApi.Models;
using EasyRecordWorkingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyRecordWorkingApi.Controllers;

[Authorize]
[Route("api/employees")]
public class EmployeesController : ApiControllerBase
{
    private readonly AppDbContext _dbContext;
    private const char TagSeparator = '|';

    public EmployeesController(AppDbContext dbContext, IUserContext userContext) : base(userContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] string? keyword,
        [FromQuery] string? type,
        [FromQuery(Name = "work_type")] string? workType,
        [FromQuery] string? tag,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 15,
        [FromQuery] string? sort = null)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (page <= 0)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 15;
        }

        pageSize = Math.Min(pageSize, 200);

        var query = _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.Deleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(e => e.Name.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(e => e.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(workType))
        {
            var trimmedWorkType = workType.Trim();
            query = query.Where(e => e.WorkType != null && e.WorkType == trimmedWorkType);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var trimmedTag = tag.Trim();
            query = query.Where(e => e.Tags != null && (
                e.Tags == trimmedTag ||
                e.Tags.StartsWith($"{trimmedTag}{TagSeparator}") ||
                e.Tags.EndsWith($"{TagSeparator}{trimmedTag}") ||
                e.Tags.Contains($"{TagSeparator}{trimmedTag}{TagSeparator}")
            ));
        }

        query = sort switch
        {
            "name_asc" => query.OrderBy(e => e.Name),
            "created_at_desc" => query.OrderByDescending(e => e.CreatedAt),
            _ => query.OrderByDescending(e => e.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeDto
            {
                Id = e.Id,
                Name = e.Name,
                Type = e.Type,
                WorkType = e.WorkType,
                Phone = e.Phone,
                IdCardNumber = e.IdCardNumber,
                Remark = e.Remark,
                Tags = ParseTags(e.Tags),
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            })
            .ToListAsync();

        var data = new PagedResult<EmployeeDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(data);
    }

    [HttpPost]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Type))
        {
            return Failure(400, 40001, "参数错误", "name 和 type 不能为空");
        }

        var name = request.Name.Trim();
        var type = request.Type.Trim();
        var workType = string.IsNullOrWhiteSpace(request.WorkType) ? null : request.WorkType.Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var idCardNumber = string.IsNullOrWhiteSpace(request.IdCardNumber) ? null : request.IdCardNumber.Trim();

        if (!IsValidEmployeeType(type))
        {
            return Failure(400, 40001, "参数错误", "type 必须为 正式工 或 临时工");
        }

        var duplicated = await _dbContext.Employees.AsNoTracking()
            .AnyAsync(e => e.TenantId == tenantId && e.Name == name && !e.Deleted);
        if (duplicated)
        {
            return Failure(409, 40901, "重复记录");
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Type = type,
            WorkType = workType,
            Phone = phone,
            IdCardNumber = idCardNumber,
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim(),
            Tags = NormalizeTagsString(request.Tags)
        };

        _dbContext.Employees.Add(employee);
        await _dbContext.SaveChangesAsync();

        var dto = new EmployeeDto
        {
            Id = employee.Id,
            Name = employee.Name,
            Type = employee.Type,
            WorkType = employee.WorkType,
            Phone = employee.Phone,
            IdCardNumber = employee.IdCardNumber,
            Remark = employee.Remark,
            Tags = ParseTags(employee.Tags),
            CreatedAt = employee.CreatedAt,
            UpdatedAt = employee.UpdatedAt
        };

        return Success(dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId && !e.Deleted);
        if (employee == null)
        {
            return Failure(404, 40401, "员工不存在");
        }

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Failure(400, 40001, "参数错误", "name 不能为空");
            }

            employee.Name = request.Name.Trim();
        }

        if (request.Type != null)
        {
            if (!IsValidEmployeeType(request.Type))
            {
                return Failure(400, 40001, "参数错误", "type 必须为 正式工 或 临时工");
            }

            employee.Type = request.Type.Trim();
        }

        if (request.WorkType != null)
        {
            employee.WorkType = string.IsNullOrWhiteSpace(request.WorkType) ? null : request.WorkType.Trim();
        }

        if (request.Phone != null)
        {
            employee.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        }

        if (request.IdCardNumber != null)
        {
            employee.IdCardNumber = string.IsNullOrWhiteSpace(request.IdCardNumber) ? null : request.IdCardNumber.Trim();
        }

        if (request.Remark != null)
        {
            employee.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
        }

        if (request.Tags != null)
        {
            employee.Tags = NormalizeTagsString(request.Tags);
        }

        await _dbContext.SaveChangesAsync();

        var dto = new EmployeeDto
        {
            Id = employee.Id,
            Name = employee.Name,
            Type = employee.Type,
            WorkType = employee.WorkType,
            Phone = employee.Phone,
            IdCardNumber = employee.IdCardNumber,
            Remark = employee.Remark,
            Tags = ParseTags(employee.Tags),
            CreatedAt = employee.CreatedAt,
            UpdatedAt = employee.UpdatedAt
        };

        return Success(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteEmployee(Guid id)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId && !e.Deleted);
        if (employee == null)
        {
            return Failure(404, 40401, "员工不存在");
        }

        employee.Deleted = true;
        await _dbContext.SaveChangesAsync();

        return Success(new { });
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportEmployees([FromForm] ImportEmployeesRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (request.File == null || request.File.Length == 0)
        {
            return Failure(400, 40001, "参数错误", "file 不能为空");
        }

        var existingNames = new HashSet<string>(
            await _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && !e.Deleted)
                .Select(e => e.Name)
                .ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        var skipped = 0;

        using var reader = new StreamReader(request.File.OpenReadStream(), Encoding.UTF8, true);
        var isFirstLine = true;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
            {
                continue;
            }

            if (isFirstLine)
            {
                isFirstLine = false;
                if (line.Contains("员工姓名"))
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                skipped++;
                continue;
            }

            var name = parts[0].Trim();
            var type = parts[1].Trim();
            var workType = parts.Length >= 3 ? parts[2].Trim() : string.Empty;
            var phone = parts.Length >= 4 ? parts[3].Trim() : string.Empty;
            var idCardNumber = parts.Length >= 5 ? parts[4].Trim() : string.Empty;
            var remarkStartIndex = parts.Length >= 5 ? 5 : parts.Length >= 4 ? 4 : parts.Length >= 3 ? 3 : 2;

            var remark = parts.Length > remarkStartIndex
                ? string.Join(",", parts, remarkStartIndex, parts.Length - remarkStartIndex).Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type) || !IsValidEmployeeType(type))
            {
                skipped++;
                continue;
            }

            if (existingNames.Contains(name))
            {
                skipped++;
                continue;
            }

            existingNames.Add(name);

            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = name,
                Type = type,
                WorkType = string.IsNullOrWhiteSpace(workType) ? null : workType,
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
                IdCardNumber = string.IsNullOrWhiteSpace(idCardNumber) ? null : idCardNumber,
                Remark = string.IsNullOrWhiteSpace(remark) ? null : remark
            };

            _dbContext.Employees.Add(employee);
            imported++;
        }

        if (imported > 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        var result = new ImportEmployeesResult
        {
            Imported = imported,
            Skipped = skipped
        };

        return Success(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportEmployees([FromQuery] string? format)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (!string.IsNullOrWhiteSpace(format) && !string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(400, 40001, "参数错误", "format 仅支持 csv");
        }

        var query = _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.Deleted);

        var employees = await query
            .OrderBy(e => e.Name)
            .ToListAsync();

        var builder = new StringBuilder();
        builder.AppendLine("员工姓名,员工类型,工种,手机号,身份证号,备注");
        foreach (var employee in employees)
        {
            var remark = employee.Remark ?? string.Empty;
            var phone = employee.Phone ?? string.Empty;
            var idCardNumber = employee.IdCardNumber ?? string.Empty;
            builder.AppendLine($"{employee.Name},{employee.Type},{employee.WorkType ?? string.Empty},{phone},{idCardNumber},{remark}");
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var fileName = $"员工管理_{DateTime.UtcNow:yyyy-MM-dd}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private static bool IsValidEmployeeType(string? type)
    {
        return string.Equals(type, "正式工", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "临时工", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeTagsString(IEnumerable<string>? tags)
    {
        if (tags == null)
        {
            return null;
        }

        var normalized = tags
            .Select(tag => tag?.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!.Replace(TagSeparator, ' ').Replace(',', ' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? null : string.Join(TagSeparator, normalized);
    }

    private static List<string> ParseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        var parts = raw.Contains(TagSeparator)
            ? raw.Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}









