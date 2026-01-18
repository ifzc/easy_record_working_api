using EasyRecordWorkingApi.Data;
using EasyRecordWorkingApi.Dtos;
using EasyRecordWorkingApi.Models;
using EasyRecordWorkingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyRecordWorkingApi.Controllers;

[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly PasswordHasher _passwordHasher;
    private readonly JwtTokenService _jwtTokenService;

    public AuthController(
        AppDbContext dbContext,
        PasswordHasher passwordHasher,
        JwtTokenService jwtTokenService,
        IUserContext userContext) : base(userContext)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Failure(400, 40001, "参数错误", "account 和 password 不能为空");
        }

        var (tenantCode, account) = ParseAccount(request.Account);
        User? user;
        Tenant? tenant;

        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            tenant = await _dbContext.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Code == tenantCode);
            if (tenant == null)
            {
                return Failure(401, 40101, "账号或密码错误");
            }

            user = await _dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Account == account);
        }
        else
        {
            var users = await _dbContext.Users.AsNoTracking()
                .Include(u => u.Tenant)
                .Where(u => u.Account == account)
                .ToListAsync();
            if (users.Count == 0)
            {
                return Failure(401, 40101, "账号或密码错误");
            }

            if (users.Count > 1)
            {
                return Failure(400, 40001, "参数错误", "账号需要包含租户信息");
            }

            user = users[0];
            tenant = user.Tenant;
        }

        if (user == null || tenant == null)
        {
            return Failure(401, 40101, "账号或密码错误");
        }

        if (!string.Equals(tenant.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(403, 40301, "租户已禁用");
        }

        if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(403, 40302, "账号已禁用");
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Failure(401, 40101, "账号或密码错误");
        }

        var token = _jwtTokenService.CreateToken(user, tenant);
        var response = new LoginResponse
        {
            Token = token,
            User = new LoginUser
            {
                Id = user.Id,
                Account = user.Account,
                DisplayName = user.DisplayName,
                Role = user.Role
            },
            Tenant = new LoginTenant
            {
                Id = tenant.Id,
                Code = tenant.Code,
                Name = tenant.Name
            }
        };

        return Success(response);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();
        if (userId == Guid.Empty || tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        var user = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId);
        var tenant = await _dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (user == null || tenant == null)
        {
            return Failure(401, 40103, "未登录");
        }

        var response = new CurrentUserResponse
        {
            User = new LoginUser
            {
                Id = user.Id,
                Account = user.Account,
                DisplayName = user.DisplayName,
                Role = user.Role
            },
            Tenant = new LoginTenant
            {
                Id = tenant.Id,
                Code = tenant.Code,
                Name = tenant.Name
            }
        };

        return Success(response);
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return Failure(400, 40001, "参数错误", "current_password 不能为空");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return Failure(400, 40001, "参数错误", "new_password 长度需至少 6 位");
        }

        var userId = GetUserId();
        var tenantId = GetTenantId();
        if (userId == Guid.Empty || tenantId == Guid.Empty)
        {
            return Failure(401, 40103, "未登录");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId);
        if (user == null)
        {
            return Failure(401, 40103, "未登录");
        }

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Failure(400, 40002, "密码错误");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _dbContext.SaveChangesAsync();

        return Success(new { });
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Success(new { });
    }

    private static (string? TenantCode, string Account) ParseAccount(string account)
    {
        var trimmed = account.Trim();
        if (trimmed.Contains('/'))
        {
            var parts = trimmed.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }
        }

        if (trimmed.Contains('@'))
        {
            var parts = trimmed.Split('@', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return (parts[1], parts[0]);
            }
        }

        return (null, trimmed);
    }
}
