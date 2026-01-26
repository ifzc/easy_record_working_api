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

    [HttpPost]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure(400, 40001, "参数错误", "name 不能为空");
        }

        var name = request.Name.Trim();
        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        var status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim();

        if (!IsValidProjectStatus(status))
        {
            return Failure(400, 40001, "参数错误", "status 必须为 active, pending, completed 或 archived");
        }

        var existingProject = await _db.Queryable<Project>()
            .FirstAsync(p => p.TenantId == tenantId && p.Name == name);
        if (existingProject != null)
        {
            if (!existingProject.Deleted)
            {
                return Failure(409, 40901, "重复记录", "项目名称已存在");
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                var codeDuplicated = await _db.Queryable<Project>()
                    .AnyAsync(p => p.TenantId == tenantId && !p.Deleted && p.Code == code && p.Id != existingProject.Id);
                if (codeDuplicated)
                {
                    return Failure(409, 40901, "重复记录", "项目名称已存在");
                }
            }

            existingProject.Deleted = false;
            existingProject.Code = code;
            existingProject.Status = status;
            existingProject.PlannedStartDate = request.PlannedStartDate;
            existingProject.PlannedEndDate = request.PlannedEndDate;
            existingProject.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
            await _db.UpdateWithTimestampAsync(existingProject);

            var restoredDto = new ProjectDto
            {
                Id = existingProject.Id,
                Name = existingProject.Name,
                Code = existingProject.Code,
                Status = existingProject.Status,
                PlannedStartDate = existingProject.PlannedStartDate,
                PlannedEndDate = existingProject.PlannedEndDate,
                Remark = existingProject.Remark,
                CreatedAt = existingProject.CreatedAt,
                UpdatedAt = existingProject.UpdatedAt
            };

            return Success(restoredDto);
        }
        if (!string.IsNullOrWhiteSpace(code))
        {
            var codeDuplicated = await _db.Queryable<Project>()
                .AnyAsync(p => p.TenantId == tenantId && !p.Deleted && p.Code == code);
            if (codeDuplicated)
            {
                return Failure(409, 40901, "重复记录", "项目代码已存在");
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

        var dto = new ProjectDto
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

        return Success(dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProject(Guid id, [FromBody] UpdateProjectRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        var project = await _db.Queryable<Project>()
            .FirstAsync(p => p.Id == id && p.TenantId == tenantId && !p.Deleted);
        if (project == null)
        {
            return Failure(404, 40401, "项目不存在");
        }

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Failure(400, 40001, "参数错误", "name 不能为空");
            }

            var name = request.Name.Trim();
            if (name != project.Name)
            {
                var duplicated = await _db.Queryable<Project>()
                    .AnyAsync(p => p.TenantId == tenantId && !p.Deleted && p.Name == name && p.Id != id);
                if (duplicated)
                {
                    return Failure(409, 40901, "重复记录", "项目名称已存在");
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
                    return Failure(409, 40901, "重复记录", "项目代码已存在");
                }
            }

            project.Code = code;
        }

        if (request.Status != null)
        {
            if (!IsValidProjectStatus(request.Status))
            {
                return Failure(400, 40001, "参数错误", "status 必须为 active, pending, completed 或 archived");
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

        var dto = new ProjectDto
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

        return Success(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        var project = await _db.Queryable<Project>()
            .FirstAsync(p => p.Id == id && p.TenantId == tenantId && !p.Deleted);
        if (project == null)
        {
            return Failure(404, 40401, "项目不存在");
        }
        project.Deleted = true;
        await _db.UpdateWithTimestampAsync(project);

        return Success(new { });
    }

    private static bool IsValidProjectStatus(string? status)
    {
        return string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase);
    }
}
