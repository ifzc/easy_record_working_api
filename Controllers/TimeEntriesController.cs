using System.Globalization;
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
[Route("api/time-entries")]
public class TimeEntriesController : ApiControllerBase
{
    private readonly AppDbContext _dbContext;

    public TimeEntriesController(AppDbContext dbContext, IUserContext userContext) : base(userContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetTimeEntries(
        [FromQuery] string? date,
        [FromQuery] string? keyword,
        [FromQuery(Name = "employee_type")] string? employeeType,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (string.IsNullOrWhiteSpace(date))
        {
            return Failure(400, 40001, "参数错误", "date 不能为空");
        }

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate))
        {
            return Failure(400, 40001, "参数错误", "date 格式应为 YYYY-MM-DD");
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

        var workDateValue = workDate.ToDateTime(TimeOnly.MinValue);
        var query = _dbContext.TimeEntries.AsNoTracking()
            .Include(t => t.Employee)
            .Where(t => t.TenantId == tenantId && t.WorkDate == workDateValue);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(t => t.Employee != null && t.Employee.Name.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(employeeType))
        {
            query = query.Where(t => t.Employee != null && t.Employee.Type == employeeType);
        }

        query = sort switch
        {
            "hours_asc" => query.OrderBy(t => t.NormalHours + t.OvertimeHours),
            "hours_desc" => query.OrderByDescending(t => t.NormalHours + t.OvertimeHours),
            _ => query.OrderByDescending(t => t.UpdatedAt)
        };

        var total = await query.CountAsync();
        var rawItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.EmployeeId,
                EmployeeName = t.Employee != null ? t.Employee.Name : string.Empty,
                EmployeeType = t.Employee != null ? t.Employee.Type : string.Empty,
                t.WorkDate,
                t.NormalHours,
                t.OvertimeHours,
                t.Remark,
                t.CreatedAt
            })
            .ToListAsync();

        var items = rawItems.Select(t => new TimeEntryDto
        {
            Id = t.Id,
            EmployeeId = t.EmployeeId,
            EmployeeName = t.EmployeeName,
            EmployeeType = t.EmployeeType,
            WorkDate = DateOnly.FromDateTime(t.WorkDate),
            NormalHours = t.NormalHours,
            OvertimeHours = t.OvertimeHours,
            Remark = t.Remark,
            CreatedAt = t.CreatedAt,
            TotalHours = t.NormalHours + t.OvertimeHours,
            WorkUnits = t.NormalHours / 8m + t.OvertimeHours / 6m
        }).ToList();

        var data = new PagedResult<TimeEntryDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(data);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTimeEntry([FromBody] CreateTimeEntryRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (!IsValidHour(request.NormalHours) || !IsValidHour(request.OvertimeHours))
        {
            return Failure(400, 40001, "参数错误", "工时必须为非负且步进为 0.5");
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (employee == null)
        {
            return Failure(404, 40401, "员工不存在");
        }

        if (!employee.IsActive)
        {
            return Failure(400, 40001, "参数错误", "员工已停用");
        }

        var workDate = request.WorkDate.ToDateTime(TimeOnly.MinValue);
        var exists = await _dbContext.TimeEntries
            .AnyAsync(t => t.TenantId == tenantId && t.EmployeeId == request.EmployeeId && t.WorkDate == workDate);
        if (exists)
        {
            return Failure(409, 40901, "重复记录");
        }

        var timeEntry = new TimeEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = request.EmployeeId,
            WorkDate = workDate,
            NormalHours = request.NormalHours,
            OvertimeHours = request.OvertimeHours,
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
        };

        _dbContext.TimeEntries.Add(timeEntry);
        await _dbContext.SaveChangesAsync();

        var dto = new TimeEntryDto
        {
            Id = timeEntry.Id,
            EmployeeId = timeEntry.EmployeeId,
            EmployeeName = employee.Name,
            EmployeeType = employee.Type,
            WorkDate = DateOnly.FromDateTime(timeEntry.WorkDate),
            NormalHours = timeEntry.NormalHours,
            OvertimeHours = timeEntry.OvertimeHours,
            Remark = timeEntry.Remark,
            CreatedAt = timeEntry.CreatedAt,
            TotalHours = timeEntry.NormalHours + timeEntry.OvertimeHours,
            WorkUnits = timeEntry.NormalHours / 8m + timeEntry.OvertimeHours / 6m
        };

        return Success(dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTimeEntry(Guid id, [FromBody] UpdateTimeEntryRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (!IsValidHour(request.NormalHours) || !IsValidHour(request.OvertimeHours))
        {
            return Failure(400, 40001, "参数错误", "工时必须为非负且步进为 0.5");
        }

        var timeEntry = await _dbContext.TimeEntries
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId);
        if (timeEntry == null)
        {
            return Failure(404, 40401, "记工不存在");
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (employee == null)
        {
            return Failure(404, 40401, "员工不存在");
        }

        var workDate = request.WorkDate.ToDateTime(TimeOnly.MinValue);
        var duplicateExists = await _dbContext.TimeEntries
            .AnyAsync(t => t.Id != id && t.TenantId == tenantId && t.EmployeeId == request.EmployeeId && t.WorkDate == workDate);
        if (duplicateExists)
        {
            return Failure(409, 40901, "重复记录");
        }

        timeEntry.EmployeeId = request.EmployeeId;
        timeEntry.WorkDate = workDate;
        timeEntry.NormalHours = request.NormalHours;
        timeEntry.OvertimeHours = request.OvertimeHours;
        if (request.Remark != null)
        {
            timeEntry.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
        }

        await _dbContext.SaveChangesAsync();

        var dto = new TimeEntryDto
        {
            Id = timeEntry.Id,
            EmployeeId = timeEntry.EmployeeId,
            EmployeeName = employee.Name,
            EmployeeType = employee.Type,
            WorkDate = DateOnly.FromDateTime(timeEntry.WorkDate),
            NormalHours = timeEntry.NormalHours,
            OvertimeHours = timeEntry.OvertimeHours,
            Remark = timeEntry.Remark,
            CreatedAt = timeEntry.CreatedAt,
            TotalHours = timeEntry.NormalHours + timeEntry.OvertimeHours,
            WorkUnits = timeEntry.NormalHours / 8m + timeEntry.OvertimeHours / 6m
        };

        return Success(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTimeEntry(Guid id)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        var timeEntry = await _dbContext.TimeEntries
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId);
        if (timeEntry == null)
        {
            return Failure(404, 40401, "记工不存在");
        }

        _dbContext.TimeEntries.Remove(timeEntry);
        await _dbContext.SaveChangesAsync();

        return Success(new { });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? month,
        [FromQuery] string? date,
        [FromQuery(Name = "employee_id")] Guid? employeeId,
        [FromQuery(Name = "employee_type")] string? employeeType)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        DateTime startDate;
        DateTime endDate;

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return Failure(400, 40001, "参数错误", "date 格式应为 YYYY-MM-DD");
            }

            startDate = parsedDate.ToDateTime(TimeOnly.MinValue);
            endDate = parsedDate.ToDateTime(TimeOnly.MinValue);
        }
        else if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMonth))
            {
                return Failure(400, 40001, "参数错误", "month 格式应为 YYYY-MM");
            }

            startDate = new DateTime(parsedMonth.Year, parsedMonth.Month, 1);
            endDate = startDate.AddMonths(1).AddDays(-1);
        }
        else
        {
            return Failure(400, 40001, "参数错误", "month 或 date 至少提供一个");
        }

        var query = _dbContext.TimeEntries.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.WorkDate >= startDate && t.WorkDate <= endDate);

        if (employeeId.HasValue)
        {
            query = query.Where(t => t.EmployeeId == employeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(employeeType))
        {
            query = query.Where(t => t.Employee != null && t.Employee.Type == employeeType);
        }

        var rawSummary = await query
            .GroupBy(t => t.WorkDate)
            .Select(g => new
            {
                g.Key,
                NormalHours = g.Sum(x => x.NormalHours),
                OvertimeHours = g.Sum(x => x.OvertimeHours),
                TotalHours = g.Sum(x => x.NormalHours + x.OvertimeHours),
                Headcount = g.Select(x => x.EmployeeId).Distinct().Count()
            })
            .OrderBy(x => x.Key)
            .ToListAsync();

        var summaryByDate = rawSummary.ToDictionary(x => DateOnly.FromDateTime(x.Key));
        var startDateOnly = DateOnly.FromDateTime(startDate);
        var endDateOnly = DateOnly.FromDateTime(endDate);
        var data = new List<TimeEntrySummaryDto>();

        for (var current = startDateOnly; current.CompareTo(endDateOnly) <= 0; current = current.AddDays(1))
        {
            if (summaryByDate.TryGetValue(current, out var summary))
            {
                data.Add(new TimeEntrySummaryDto
                {
                    Date = current,
                    NormalHours = summary.NormalHours,
                    OvertimeHours = summary.OvertimeHours,
                    TotalHours = summary.TotalHours,
                    TotalWorkUnits = summary.NormalHours / 8m + summary.OvertimeHours / 6m,
                    Headcount = summary.Headcount
                });
                continue;
            }

            data.Add(new TimeEntrySummaryDto
            {
                Date = current,
                NormalHours = 0m,
                OvertimeHours = 0m,
                TotalHours = 0m,
                TotalWorkUnits = 0m,
                Headcount = 0
            });
        }

        return Success(data);
    }

    private static bool IsValidHour(decimal value)
    {
        if (value < 0)
        {
            return false;
        }

        var scaled = value * 2;
        return decimal.Truncate(scaled) == scaled;
    }
}
