using ClosedXML.Excel;
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
[Route("api/projects")]
public class ProjectsController : ApiControllerBase
{
    private const string UnauthorizedMessage = "\u672a\u767b\u5f55";
    private const string BadRequestMessage = "\u53c2\u6570\u9519\u8bef";
    private const string DuplicateRecordMessage = "\u91cd\u590d\u8bb0\u5f55";
    private const string ProjectNameExistsMessage = "\u9879\u76ee\u540d\u79f0\u5df2\u5b58\u5728";
    private const string ProjectCodeExistsMessage = "\u9879\u76ee\u4ee3\u7801\u5df2\u5b58\u5728";
    private const string ProjectNotFoundMessage = "\u9879\u76ee\u4e0d\u5b58\u5728";
    private const string EmptyNameMessage = "name \u4e0d\u80fd\u4e3a\u7a7a";
    private const string InvalidStatusMessage = "status \u5fc5\u987b\u4e3a active, pending, completed \u6216 archived";
    private readonly ISqlSugarClient _db;

    public ProjectsController(ISqlSugarClient db, IUserContext userContext) : base(userContext)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects(
        [FromQuery] string? keyword,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 15,
        [FromQuery] string? sort = null)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, UnauthorizedMessage);
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

        var query = BuildProjectQuery(tenantId, keyword, status);

        query = sort switch
        {
            "name_asc" => query.OrderBy(p => p.Name),
            "created_at_desc" => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderByDescending(p => p.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProjectDto
            {
                Id = p.Id,
                Name = p.Name,
                Code = p.Code,
                Status = p.Status,
                PlannedStartDate = p.PlannedStartDate,
                PlannedEndDate = p.PlannedEndDate,
                Remark = p.Remark,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        var data = new PagedResult<ProjectDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(data);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportProjects(
        [FromQuery] string? keyword,
        [FromQuery] string? status,
        [FromQuery] string? format)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, UnauthorizedMessage);
        }

        if (!string.IsNullOrWhiteSpace(format) && !string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(400, 40001, BadRequestMessage, "format \u4ec5\u652f\u6301 xlsx");
        }

        var projects = await BuildProjectQuery(tenantId, keyword, status)
            .OrderBy(p => p.Name)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("\u9879\u76ee\u7ba1\u7406");
        var headers = new[]
        {
            "\u9879\u76ee\u540d\u79f0",
            "\u9879\u76ee\u4ee3\u7801",
            "\u9879\u76ee\u72b6\u6001",
            "\u8ba1\u5212\u5f00\u59cb",
            "\u8ba1\u5212\u7ed3\u675f",
            "\u5907\u6ce8",
            "\u521b\u5efa\u65f6\u95f4"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            worksheet.Cell(1, index + 1).Value = headers[index];
        }

        var rowIndex = 2;
        foreach (var project in projects)
        {
            worksheet.Cell(rowIndex, 1).Value = project.Name;
            worksheet.Cell(rowIndex, 2).Value = project.Code ?? string.Empty;
            worksheet.Cell(rowIndex, 3).Value = GetProjectStatusLabel(project.Status);
            SetDateOnlyCellValue(worksheet.Cell(rowIndex, 4), project.PlannedStartDate, "yyyy-MM-dd");
            SetDateOnlyCellValue(worksheet.Cell(rowIndex, 5), project.PlannedEndDate, "yyyy-MM-dd");
            worksheet.Cell(rowIndex, 6).Value = project.Remark ?? string.Empty;
            SetDateTimeCellValue(worksheet.Cell(rowIndex, 7), project.CreatedAt, "yyyy-MM-dd HH:mm:ss");
            rowIndex++;
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
        worksheet.SheetView.FreezeRows(1);
        worksheet.RangeUsed()?.SetAutoFilter();
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"\u9879\u76ee\u7ba1\u7406_{DateTime.UtcNow:yyyy-MM-dd}.xlsx";
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName
        );
    }

    [HttpPost]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, UnauthorizedMessage);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure(400, 40001, BadRequestMessage, EmptyNameMessage);
        }

        var name = request.Name.Trim();
        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        var status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim();

        if (!IsValidProjectStatus(status))
        {
            return Failure(400, 40001, BadRequestMessage, InvalidStatusMessage);
        }

        var existingProject = await _db.Queryable<Project>()
            .FirstAsync(p => p.TenantId == tenantId && p.Name == name);
        if (existingProject != null)
        {
            if (!existingProject.Deleted)
            {
                return Failure(409, 40901, DuplicateRecordMessage, ProjectNameExistsMessage);
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                var codeDuplicated = await _db.Queryable<Project>()
                    .AnyAsync(p => p.TenantId == tenantId && !p.Deleted && p.Code == code && p.Id != existingProject.Id);
                if (codeDuplicated)
                {
                    return Failure(409, 40901, DuplicateRecordMessage, ProjectCodeExistsMessage);
                }
            }

            existingProject.Deleted = false;
            existingProject.Code = code;
            existingProject.Status = status;
            existingProject.PlannedStartDate = request.PlannedStartDate;
            existingProject.PlannedEndDate = request.PlannedEndDate;
            existingProject.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
            await _db.UpdateWithTimestampAsync(existingProject);

            return Success(ToProjectDto(existingProject));
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var codeDuplicated = await _db.Queryable<Project>()
                .AnyAsync(p => p.TenantId == tenantId && !p.Deleted && p.Code == code);
            if (codeDuplicated)
            {
                return Failure(409, 40901, DuplicateRecordMessage, ProjectCodeExistsMessage);
            }
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Code = code,
            Status = status,
            PlannedStartDate = request.PlannedStartDate,
            PlannedEndDate = request.PlannedEndDate,
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
        };

        await _db.InsertWithTimestampAsync(project);

        return Success(ToProjectDto(project));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProject(Guid id, [FromBody] UpdateProjectRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, UnauthorizedMessage);
        }

        var project = await _db.Queryable<Project>()
            .FirstAsync(p => p.Id == id && p.TenantId == tenantId && !p.Deleted);
        if (project == null)
        {
            return Failure(404, 40401, ProjectNotFoundMessage);
        }

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Failure(400, 40001, BadRequestMessage, EmptyNameMessage);
            }

            var name = request.Name.Trim();
            if (name != project.Name)
            {
                var duplicated = await _db.Queryable<Project>()
                    .AnyAsync(p => p.TenantId == tenantId && !p.Deleted && p.Name == name && p.Id != id);
                if (duplicated)
                {
                    return Failure(409, 40901, DuplicateRecordMessage, ProjectNameExistsMessage);
                }
            }

            project.Name = name;
        }

        if (request.Code != null)
        {
            var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
            if (code != project.Code && !string.IsNullOrWhiteSpace(code))
            {
                var codeDuplicated = await _db.Queryable<Project>()
                    .AnyAsync(p => p.TenantId == tenantId && !p.Deleted && p.Code == code && p.Id != id);
                if (codeDuplicated)
                {
                    return Failure(409, 40901, DuplicateRecordMessage, ProjectCodeExistsMessage);
                }
            }

            project.Code = code;
        }

        if (request.Status != null)
        {
            if (!IsValidProjectStatus(request.Status))
            {
                return Failure(400, 40001, BadRequestMessage, InvalidStatusMessage);
            }

            project.Status = request.Status.Trim();
        }

        if (request.PlannedStartDate != null)
        {
            project.PlannedStartDate = request.PlannedStartDate;
        }

        if (request.PlannedEndDate != null)
        {
            project.PlannedEndDate = request.PlannedEndDate;
        }

        if (request.Remark != null)
        {
            project.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
        }

        await _db.UpdateWithTimestampAsync(project);

        return Success(ToProjectDto(project));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, UnauthorizedMessage);
        }

        var project = await _db.Queryable<Project>()
            .FirstAsync(p => p.Id == id && p.TenantId == tenantId && !p.Deleted);
        if (project == null)
        {
            return Failure(404, 40401, ProjectNotFoundMessage);
        }

        project.Deleted = true;
        await _db.UpdateWithTimestampAsync(project);

        return Success(new { });
    }

    private ISugarQueryable<Project> BuildProjectQuery(Guid tenantId, string? keyword, string? status)
    {
        var query = _db.Queryable<Project>()
            .Where(p => p.TenantId == tenantId && !p.Deleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(p => p.Name.Contains(keyword) || (p.Code != null && p.Code.Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(p => p.Status == status);
        }

        return query;
    }

    private static ProjectDto ToProjectDto(Project project)
    {
        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Code = project.Code,
            Status = project.Status,
            PlannedStartDate = project.PlannedStartDate,
            PlannedEndDate = project.PlannedEndDate,
            Remark = project.Remark,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    private static void SetDateOnlyCellValue(IXLCell cell, DateOnly? value, string format)
    {
        if (!value.HasValue || value.Value.Year < 100)
        {
            cell.Value = string.Empty;
            return;
        }

        cell.Value = value.Value.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = format;
    }

    private static void SetDateTimeCellValue(IXLCell cell, DateTime value, string format)
    {
        if (value.Year < 100)
        {
            cell.Value = string.Empty;
            return;
        }

        cell.Value = value;
        cell.Style.DateFormat.Format = format;
    }

    private static string GetProjectStatusLabel(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "active" => "\u8fdb\u884c\u4e2d",
            "pending" => "\u5f85\u5f00\u59cb",
            "completed" => "\u5df2\u5b8c\u6210",
            "archived" => "\u5df2\u5f52\u6863",
            _ => status ?? string.Empty
        };
    }

    private static bool IsValidProjectStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() is "active" or "pending" or "completed" or "archived";
    }
}
