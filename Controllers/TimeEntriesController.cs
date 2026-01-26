using System.Globalization;
using EasyRecordWorkingApi.Contracts;
using EasyRecordWorkingApi.Data;
using EasyRecordWorkingApi.Dtos;
using EasyRecordWorkingApi.Models;
using EasyRecordWorkingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace EasyRecordWorkingApi.Controllers;

[Authorize]
[Route("api/time-entries")]
public class TimeEntriesController : ApiControllerBase
{
    private readonly ISqlSugarClient _db;

    public TimeEntriesController(ISqlSugarClient db, IUserContext userContext) : base(userContext)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetTimeEntries(
        [FromQuery] string? date,
        [FromQuery] string? keyword,
        [FromQuery(Name = "employee_id")] Guid? employeeId,
        [FromQuery(Name = "employee_type")] string? employeeType,
        [FromQuery(Name = "work_type")] string? workType,
        [FromQuery(Name = "project_id")] Guid? projectId,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 15)
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
            pageSize = 15;
        }

        pageSize = Math.Min(pageSize, 200);

        var workDateValue = workDate.ToDateTime(TimeOnly.MinValue);

        // Build base query for time entries
        var timeEntryQuery = _db.Queryable<TimeEntry>()
            .Where(t => t.TenantId == tenantId && !t.Deleted && t.WorkDate == workDateValue);

        if (employeeId.HasValue)
        {
            timeEntryQuery = timeEntryQuery.Where(t => t.EmployeeId == employeeId.Value);
        }

        if (projectId.HasValue)
        {
            timeEntryQuery = timeEntryQuery.Where(t => t.ProjectId == projectId.Value);
        }

        // Get employee IDs for filtering
        var employeeQuery = _db.Queryable<Employee>()
            .Where(e => e.TenantId == tenantId && !e.Deleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            employeeQuery = employeeQuery.Where(e => e.Name.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(employeeType))
        {
            employeeQuery = employeeQuery.Where(e => e.Type == employeeType);
        }

        if (!string.IsNullOrWhiteSpace(workType))
        {
            var trimmedWorkType = workType.Trim();
            employeeQuery = employeeQuery.Where(e => e.WorkType == trimmedWorkType);
        }

        var filteredEmployeeIds = await employeeQuery.Select(e => e.Id).ToListAsync();
        timeEntryQuery = timeEntryQuery.Where(t => filteredEmployeeIds.Contains(t.EmployeeId));

        timeEntryQuery = sort switch
        {
            "hours_asc" => timeEntryQuery.OrderBy(t => t.NormalHours + t.OvertimeHours),
            "hours_desc" => timeEntryQuery.OrderByDescending(t => t.NormalHours + t.OvertimeHours),
            _ => timeEntryQuery.OrderByDescending(t => t.UpdatedAt)
        };

        var total = await timeEntryQuery.CountAsync();
        var timeEntries = await timeEntryQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Fetch related employees and projects
        var relatedEmployeeIds = timeEntries.Select(t => t.EmployeeId).Distinct().ToList();
        var relatedProjectIds = timeEntries.Where(t => t.ProjectId.HasValue).Select(t => t.ProjectId!.Value).Distinct().ToList();

        var employeesDict = (await _db.Queryable<Employee>()
            .Where(e => relatedEmployeeIds.Contains(e.Id))
            .ToListAsync())
            .ToDictionary(e => e.Id);

        var projectsDict = relatedProjectIds.Count > 0
            ? (await _db.Queryable<Project>()
                .Where(p => relatedProjectIds.Contains(p.Id))
                .ToListAsync())
                .ToDictionary(p => p.Id)
            : new Dictionary<Guid, Project>();

        var items = timeEntries.Select(t =>
        {
            employeesDict.TryGetValue(t.EmployeeId, out var emp);
            Project? proj = null;
            if (t.ProjectId.HasValue)
            {
                projectsDict.TryGetValue(t.ProjectId.Value, out proj);
            }

            return new TimeEntryDto
            {
                Id = t.Id,
                EmployeeId = t.EmployeeId,
                EmployeeName = emp?.Name ?? string.Empty,
                EmployeeType = emp?.Type ?? string.Empty,
                WorkType = emp?.WorkType ?? string.Empty,
                ProjectId = t.ProjectId,
                ProjectName = proj?.Name,
                WorkDate = DateOnly.FromDateTime(t.WorkDate),
                NormalHours = t.NormalHours,
                OvertimeHours = t.OvertimeHours,
                Remark = t.Remark,
                CreatedAt = t.CreatedAt,
                TotalHours = t.NormalHours + t.OvertimeHours,
                WorkUnits = t.NormalHours / 8m + t.OvertimeHours / 6m
            };
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

        var employee = await _db.Queryable<Employee>()
            .FirstAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId && !e.Deleted);
        if (employee == null)
        {
            return Failure(404, 40401, "员工不存在");
        }

        var project = await GetProjectAsync(request.ProjectId, tenantId);
        if (request.ProjectId.HasValue && project == null)
        {
            return Failure(404, 40401, "项目不存在");
        }

        var workDate = request.WorkDate.ToDateTime(TimeOnly.MinValue);
        var existing = await _db.Queryable<TimeEntry>()
            .FirstAsync(t => t.TenantId == tenantId && t.EmployeeId == request.EmployeeId && t.WorkDate == workDate);
        if (existing != null)
        {
            if (!existing.Deleted)
            {
                return Failure(409, 40901, "重复记录");
            }

            existing.Deleted = false;
            existing.NormalHours = request.NormalHours;
            existing.OvertimeHours = request.OvertimeHours;
            existing.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
            existing.ProjectId = request.ProjectId;
            await _db.UpdateWithTimestampAsync(existing);

            var restoredDto = new TimeEntryDto
            {
                Id = existing.Id,
                EmployeeId = existing.EmployeeId,
                EmployeeName = employee.Name,
                EmployeeType = employee.Type,
                WorkType = employee.WorkType,
                ProjectId = existing.ProjectId,
                ProjectName = project?.Name,
                WorkDate = DateOnly.FromDateTime(existing.WorkDate),
                NormalHours = existing.NormalHours,
                OvertimeHours = existing.OvertimeHours,
                Remark = existing.Remark,
                CreatedAt = existing.CreatedAt,
                TotalHours = existing.NormalHours + existing.OvertimeHours,
                WorkUnits = existing.NormalHours / 8m + existing.OvertimeHours / 6m
            };

            return Success(restoredDto);
        }
        var timeEntry = new TimeEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = request.EmployeeId,
            ProjectId = request.ProjectId,
            WorkDate = workDate,
            NormalHours = request.NormalHours,
            OvertimeHours = request.OvertimeHours,
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
        };

        await _db.InsertWithTimestampAsync(timeEntry);

        var dto = new TimeEntryDto
        {
            Id = timeEntry.Id,
            EmployeeId = timeEntry.EmployeeId,
            EmployeeName = employee.Name,
            EmployeeType = employee.Type,
            WorkType = employee.WorkType,
            ProjectId = timeEntry.ProjectId,
            ProjectName = project?.Name,
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

    [HttpPost("batch")]
    public async Task<IActionResult> BatchCreateTimeEntries([FromBody] BatchCreateTimeEntriesRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (request.EmployeeIds == null || request.EmployeeIds.Count == 0)
        {
            return Failure(400, 40001, "参数错误", "employee_ids 不能为空");
        }

        if (request.WorkDates == null || request.WorkDates.Count == 0)
        {
            return Failure(400, 40001, "参数错误", "work_dates 不能为空");
        }

        if (!IsValidHour(request.NormalHours) || !IsValidHour(request.OvertimeHours))
        {
            return Failure(400, 40001, "参数错误", "工时必须为非负且步进为 0.5");
        }

        var project = await GetProjectAsync(request.ProjectId, tenantId);
        if (request.ProjectId.HasValue && project == null)
        {
            return Failure(404, 40401, "项目不存在");
        }

        var employeeIds = request.EmployeeIds.Distinct().ToList();
        var employees = await _db.Queryable<Employee>()
            .Where(e => e.TenantId == tenantId && !e.Deleted && employeeIds.Contains(e.Id))
            .ToListAsync();

        var employeeMap = employees.ToDictionary(e => e.Id);
        var workDates = request.WorkDates.Distinct().ToList();
        var total = employeeIds.Count * workDates.Count;
        var created = 0;
        var skipped = 0;
        var details = new List<BatchCreateTimeEntryDetail>();

        foreach (var employeeId in employeeIds)
        {
            if (!employeeMap.TryGetValue(employeeId, out var employee))
            {
                foreach (var workDate in workDates)
                {
                    details.Add(new BatchCreateTimeEntryDetail
                    {
                        EmployeeId = employeeId,
                        WorkDate = workDate,
                        Status = "skipped",
                        Reason = "员工不存在"
                    });
                    skipped++;
                }
                continue;
            }

        foreach (var workDate in workDates)
            {
                var workDateTime = workDate.ToDateTime(TimeOnly.MinValue);
                var existing = await _db.Queryable<TimeEntry>()
                    .FirstAsync(t => t.TenantId == tenantId && t.EmployeeId == employeeId && t.WorkDate == workDateTime);
                if (existing != null)
                {
                    if (!existing.Deleted)
                    {
                        details.Add(new BatchCreateTimeEntryDetail
                        {
                            EmployeeId = employeeId,
                            WorkDate = workDate,
                            Status = "skipped",
                            Reason = "记录已存在"
                        });
                        skipped++;
                        continue;
                    }

                    existing.Deleted = false;
                    existing.NormalHours = request.NormalHours;
                    existing.OvertimeHours = request.OvertimeHours;
                    existing.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
                    existing.ProjectId = request.ProjectId;
                    details.Add(new BatchCreateTimeEntryDetail
                    {
                        EmployeeId = employeeId,
                        WorkDate = workDate,
                        Status = "created",
                        Reason = null
                    });
                    created++;
                    continue;
                }
                var timeEntry = new TimeEntry
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    ProjectId = request.ProjectId,
                    WorkDate = workDateTime,
                    NormalHours = request.NormalHours,
                    OvertimeHours = request.OvertimeHours,
                    Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
                };

                await _db.InsertWithTimestampAsync(timeEntry);
                details.Add(new BatchCreateTimeEntryDetail
                {
                    EmployeeId = employeeId,
                    WorkDate = workDate,
                    Status = "created",
                    Reason = null
                });
                created++;
            }
        }

        // SaveChanges already done per insert above

        var result = new BatchCreateTimeEntriesResult
        {
            Total = total,
            Created = created,
            Skipped = skipped,
            Details = details
        };

        return Success(result);
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

        var timeEntry = await _db.Queryable<TimeEntry>()
            .FirstAsync(t => t.Id == id && t.TenantId == tenantId);
        if (timeEntry == null)
        {
            return Failure(404, 40401, "记工不存在");
        }

        var employee = await _db.Queryable<Employee>()
            .FirstAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (employee == null)
        {
            return Failure(404, 40401, "员工不存在");
        }

        var project = await GetProjectAsync(request.ProjectId, tenantId);
        if (request.ProjectId.HasValue && project == null)
        {
            return Failure(404, 40401, "项目不存在");
        }

        var workDate = request.WorkDate.ToDateTime(TimeOnly.MinValue);
        var duplicateExists = await _db.Queryable<TimeEntry>()
            .AnyAsync(t => t.Id != id && t.TenantId == tenantId && !t.Deleted && t.EmployeeId == request.EmployeeId && t.WorkDate == workDate);
        if (duplicateExists)
        {
            return Failure(409, 40901, "重复记录");
        }

        timeEntry.EmployeeId = request.EmployeeId;
        timeEntry.ProjectId = request.ProjectId;
        timeEntry.WorkDate = workDate;
        timeEntry.NormalHours = request.NormalHours;
        timeEntry.OvertimeHours = request.OvertimeHours;
        if (request.Remark != null)
        {
            timeEntry.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
        }

        await _db.UpdateWithTimestampAsync(timeEntry);

        var dto = new TimeEntryDto
        {
            Id = timeEntry.Id,
            EmployeeId = timeEntry.EmployeeId,
            EmployeeName = employee.Name,
            EmployeeType = employee.Type,
            WorkType = employee.WorkType,
            ProjectId = timeEntry.ProjectId,
            ProjectName = project?.Name,
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

        var timeEntry = await _db.Queryable<TimeEntry>()
            .FirstAsync(t => t.Id == id && t.TenantId == tenantId && !t.Deleted);
        if (timeEntry == null)
        {
            return Failure(404, 40401, "记工不存在");
        }

        timeEntry.Deleted = true;
        await _db.UpdateWithTimestampAsync(timeEntry);

        return Success(new { });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? month,
        [FromQuery] string? date,
        [FromQuery(Name = "employee_id")] Guid? employeeId,
        [FromQuery(Name = "employee_type")] string? employeeType,
        [FromQuery(Name = "work_type")] string? workType,
        [FromQuery(Name = "project_id")] Guid? projectId)
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

        var query = _db.Queryable<TimeEntry>()
            .Where(t => t.TenantId == tenantId && !t.Deleted && t.WorkDate >= startDate && t.WorkDate <= endDate);

        if (employeeId.HasValue)
        {
            query = query.Where(t => t.EmployeeId == employeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(employeeType))
        {
            var empIds = await _db.Queryable<Employee>()
                .Where(e => e.TenantId == tenantId && !e.Deleted && e.Type == employeeType)
                .Select(e => e.Id)
                .ToListAsync();
            query = query.Where(t => empIds.Contains(t.EmployeeId));
        }

        if (!string.IsNullOrWhiteSpace(workType))
        {
            var trimmedWorkType = workType.Trim();
            var empIds = await _db.Queryable<Employee>()
                .Where(e => e.TenantId == tenantId && !e.Deleted && e.WorkType == trimmedWorkType)
                .Select(e => e.Id)
                .ToListAsync();
            query = query.Where(t => empIds.Contains(t.EmployeeId));
        }

        if (projectId.HasValue)
        {
            query = query.Where(t => t.ProjectId == projectId.Value);
        }

        var rawSummary = await query
            .GroupBy(t => t.WorkDate)
            .Select(g => new
            {
                Key = g.WorkDate,
                NormalHours = SqlFunc.AggregateSum(g.NormalHours),
                OvertimeHours = SqlFunc.AggregateSum(g.OvertimeHours),
                TotalHours = SqlFunc.AggregateSum(g.NormalHours + g.OvertimeHours),
                Headcount = SqlFunc.AggregateDistinctCount(g.EmployeeId)
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

    [HttpGet("summary/project-units")]
    public async Task<IActionResult> GetProjectWorkUnits(
        [FromQuery] string? month,
        [FromQuery] string? date,
        [FromQuery(Name = "employee_id")] Guid? employeeId,
        [FromQuery(Name = "employee_type")] string? employeeType,
        [FromQuery(Name = "work_type")] string? workType,
        [FromQuery(Name = "project_id")] Guid? projectId)
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

        var query2 = _db.Queryable<TimeEntry>()
            .Where(t => t.TenantId == tenantId && !t.Deleted && t.WorkDate >= startDate && t.WorkDate <= endDate);

        if (employeeId.HasValue)
        {
            query2 = query2.Where(t => t.EmployeeId == employeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(employeeType))
        {
            var empIds = await _db.Queryable<Employee>()
                .Where(e => e.TenantId == tenantId && !e.Deleted && e.Type == employeeType)
                .Select(e => e.Id)
                .ToListAsync();
            query2 = query2.Where(t => empIds.Contains(t.EmployeeId));
        }

        if (!string.IsNullOrWhiteSpace(workType))
        {
            var trimmedWorkType = workType.Trim();
            var empIds = await _db.Queryable<Employee>()
                .Where(e => e.TenantId == tenantId && !e.Deleted && e.WorkType == trimmedWorkType)
                .Select(e => e.Id)
                .ToListAsync();
            query2 = query2.Where(t => empIds.Contains(t.EmployeeId));
        }

        if (projectId.HasValue)
        {
            query2 = query2.Where(t => t.ProjectId == projectId.Value);
        }

        var timeEntries = await query2.ToListAsync();
        var projectIds = timeEntries.Where(t => t.ProjectId.HasValue).Select(t => t.ProjectId!.Value).Distinct().ToList();
        var projects = await _db.Queryable<Project>()
            .Where(p => projectIds.Contains(p.Id))
            .ToListAsync();
        var projectMap = projects.ToDictionary(p => p.Id, p => p.Name);

        var data = timeEntries
            .GroupBy(t => t.ProjectId)
            .Select(g => new ProjectWorkUnitSummaryDto
            {
                ProjectId = g.Key,
                ProjectName = g.Key.HasValue && projectMap.TryGetValue(g.Key.Value, out var name) ? name : "未关联项目",
                WorkUnits = g.Sum(x => x.NormalHours / 8m + x.OvertimeHours / 6m)
            })
            .OrderByDescending(x => x.WorkUnits)
            .ToList();

        return Success(data);
    }

    [HttpGet("summary/employee-units")]
    public async Task<IActionResult> GetEmployeeWorkUnits(
        [FromQuery] string? month,
        [FromQuery] string? date,
        [FromQuery(Name = "employee_id")] Guid? employeeId,
        [FromQuery(Name = "employee_type")] string? employeeType,
        [FromQuery(Name = "work_type")] string? workType,
        [FromQuery(Name = "project_id")] Guid? projectId)
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

        var query3 = _db.Queryable<TimeEntry>()
            .Where(t => t.TenantId == tenantId && !t.Deleted && t.WorkDate >= startDate && t.WorkDate <= endDate);

        if (employeeId.HasValue)
        {
            query3 = query3.Where(t => t.EmployeeId == employeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(employeeType))
        {
            var empIds = await _db.Queryable<Employee>()
                .Where(e => e.TenantId == tenantId && !e.Deleted && e.Type == employeeType)
                .Select(e => e.Id)
                .ToListAsync();
            query3 = query3.Where(t => empIds.Contains(t.EmployeeId));
        }

        if (!string.IsNullOrWhiteSpace(workType))
        {
            var trimmedWorkType = workType.Trim();
            var empIds = await _db.Queryable<Employee>()
                .Where(e => e.TenantId == tenantId && !e.Deleted && e.WorkType == trimmedWorkType)
                .Select(e => e.Id)
                .ToListAsync();
            query3 = query3.Where(t => empIds.Contains(t.EmployeeId));
        }

        if (projectId.HasValue)
        {
            query3 = query3.Where(t => t.ProjectId == projectId.Value);
        }

        var timeEntries3 = await query3.ToListAsync();
        var employeeIds3 = timeEntries3.Select(t => t.EmployeeId).Distinct().ToList();
        var employees = await _db.Queryable<Employee>()
            .Where(e => employeeIds3.Contains(e.Id))
            .ToListAsync();
        var employeeMap = employees.ToDictionary(e => e.Id, e => e.Name);

        var data = timeEntries3
            .GroupBy(t => t.EmployeeId)
            .Select(g => new EmployeeWorkUnitSummaryDto
            {
                EmployeeId = g.Key,
                EmployeeName = employeeMap.TryGetValue(g.Key, out var name) ? name : "未知员工",
                WorkUnits = g.Sum(x => x.NormalHours / 8m + x.OvertimeHours / 6m)
            })
            .OrderByDescending(x => x.WorkUnits)
            .ToList();

        return Success(data);
    }

    private async Task<Project?> GetProjectAsync(Guid? projectId, Guid tenantId)
    {
        if (!projectId.HasValue || projectId.Value == Guid.Empty)
        {
            return null;
        }

        return await _db.Queryable<Project>()
            .FirstAsync(p => p.Id == projectId.Value && p.TenantId == tenantId && !p.Deleted);
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





















