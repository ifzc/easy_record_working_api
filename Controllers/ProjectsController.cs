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
[Route("api/projects")]
public class ProjectsController : ApiControllerBase
{
    private readonly AppDbContext _dbContext;

    public ProjectsController(AppDbContext dbContext, IUserContext userContext) : base(userContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects(
        [FromQuery] string? keyword,
        [FromQuery] string? status,
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

        var query = _dbContext.Projects.AsNoTracking()
            .Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(p => p.Name.Contains(keyword) || (p.Code != null && p.Code.Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (isActive.HasValue)
        {
            query = query.Where(p => p.IsActive == isActive.Value);
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
                IsActive = p.IsActive,
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

        var duplicated = await _dbContext.Projects.AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId && p.Name == name);
        if (duplicated)
        {
            return Failure(409, 40901, "重复记录", "项目名称已存在");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var codeDuplicated = await _dbContext.Projects.AsNoTracking()
                .AnyAsync(p => p.TenantId == tenantId && p.Code == code);
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
            IsActive = true,
            PlannedStartDate = request.PlannedStartDate,
            PlannedEndDate = request.PlannedEndDate,
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync();

        var dto = new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Code = project.Code,
            Status = project.Status,
            IsActive = project.IsActive,
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

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
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
                var duplicated = await _dbContext.Projects.AsNoTracking()
                    .AnyAsync(p => p.TenantId == tenantId && p.Name == name && p.Id != id);
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
                var codeDuplicated = await _dbContext.Projects.AsNoTracking()
                    .AnyAsync(p => p.TenantId == tenantId && p.Code == code && p.Id != id);
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

        if (request.IsActive.HasValue)
        {
            project.IsActive = request.IsActive.Value;
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

        await _dbContext.SaveChangesAsync();

        var dto = new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Code = project.Code,
            Status = project.Status,
            IsActive = project.IsActive,
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

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (project == null)
        {
            return Failure(404, 40401, "项目不存在");
        }

        project.IsActive = false;
        await _dbContext.SaveChangesAsync();

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
