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

    public EmployeesController(AppDbContext dbContext, IUserContext userContext) : base(userContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] string? keyword,
        [FromQuery] string? type,
        [FromQuery(Name = "is_active")] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20,
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
            pageSize = 20;
        }

        pageSize = Math.Min(pageSize, 200);

        var query = _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(e => e.Name.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(e => e.Type == type);
        }

        if (isActive.HasValue)
        {
            query = query.Where(e => e.IsActive == isActive.Value);
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
                IsActive = e.IsActive,
                Remark = e.Remark,
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

        if (!IsValidEmployeeType(request.Type))
        {
            return Failure(400, 40001, "参数错误", "type 必须为 正式工 或 临时工");
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Type = request.Type.Trim(),
            IsActive = true,
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
        };

        _dbContext.Employees.Add(employee);
        await _dbContext.SaveChangesAsync();

        var dto = new EmployeeDto
        {
            Id = employee.Id,
            Name = employee.Name,
            Type = employee.Type,
            IsActive = employee.IsActive,
            Remark = employee.Remark,
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
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
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

        if (request.IsActive.HasValue)
        {
            employee.IsActive = request.IsActive.Value;
        }

        if (request.Remark != null)
        {
            employee.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
        }

        await _dbContext.SaveChangesAsync();

        var dto = new EmployeeDto
        {
            Id = employee.Id,
            Name = employee.Name,
            Type = employee.Type,
            IsActive = employee.IsActive,
            Remark = employee.Remark,
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
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee == null)
        {
            return Failure(404, 40401, "员工不存在");
        }

        employee.IsActive = false;
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
            var remarkStartIndex = 2;
            if (parts.Length >= 3 && bool.TryParse(parts[2], out _))
            {
                remarkStartIndex = 3;
            }

            var remark = parts.Length > remarkStartIndex
                ? string.Join(",", parts, remarkStartIndex, parts.Length - remarkStartIndex).Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type) || !IsValidEmployeeType(type))
            {
                skipped++;
                continue;
            }

            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = name,
                Type = type,
                IsActive = true,
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
    public async Task<IActionResult> ExportEmployees([FromQuery] string? format, [FromQuery(Name = "is_active")] bool? isActive)
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
            .Where(e => e.TenantId == tenantId);

        if (isActive.HasValue)
        {
            query = query.Where(e => e.IsActive == isActive.Value);
        }

        var employees = await query
            .OrderBy(e => e.Name)
            .ToListAsync();

        var builder = new StringBuilder();
        builder.AppendLine("员工姓名,员工类型,是否有效,备注");
        foreach (var employee in employees)
        {
            var activeText = employee.IsActive ? "true" : "false";
            var remark = employee.Remark ?? string.Empty;
            builder.AppendLine($"{employee.Name},{employee.Type},{activeText},{remark}");
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
}
