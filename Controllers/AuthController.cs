using System.Text;
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
            tenant = await _dbContext.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == user.TenantId);
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

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Account)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return Failure(400, 40001, "参数错误", "account 和 password 不能为空");
        }

        if (request.Password.Length < 6)
        {
            return Failure(400, 40001, "参数错误", "password 长度需至少 6 位");
        }

        var account = request.Account.Trim();
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        var tenantName = string.IsNullOrWhiteSpace(request.TenantName) ? string.Empty : request.TenantName.Trim();

        if (string.IsNullOrWhiteSpace(tenantName))
        {
            tenantName = string.IsNullOrWhiteSpace(displayName) ? account : displayName;
        }

        if (tenantName.Length > 100)
        {
            return Failure(400, 40001, "参数错误", "tenant_name 长度不能超过 100");
        }

        if (account.Contains('/') || account.Contains('@'))
        {
            return Failure(400, 40001, "参数错误", "account 不能包含 '/' 或 '@'");
        }

        if (account.Length > 100)
        {
            return Failure(400, 40001, "参数错误", "account 长度不能超过 100");
        }

        var accountExists = await _dbContext.Users.AsNoTracking()
            .AnyAsync(u => u.Account == account);
        if (accountExists)
        {
            return Failure(409, 40901, "账户已存在");
        }

        if (displayName != null && displayName.Length > 100)
        {
            return Failure(400, 40001, "参数错误", "display_name 长度不能超过 100");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Code = GenerateTenantCode(tenantName),
            Name = tenantName,
            Status = "active"
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Account = account,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = displayName,
            Role = "member",
            Status = "active"
        };

        _dbContext.Tenants.Add(tenant);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

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
    [HttpPost("change-display-name")]
    public async Task<IActionResult> ChangeDisplayName([FromBody] ChangeDisplayNameRequest request)
    {
        if (request.DisplayName == null)
        {
            return Failure(400, 40001, "参数错误", "display_name 不能为空");
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (displayName != null && displayName.Length > 100)
        {
            return Failure(400, 40001, "参数错误", "display_name 长度不能超过 100");
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

        user.DisplayName = displayName;
        await _dbContext.SaveChangesAsync();

        return Success(new
        {
            DisplayName = user.DisplayName
        });
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

    private static string GenerateTenantCode(string tenantName)
    {
        var baseCode = BuildTenantCodeBase(tenantName);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var allowedBaseLength = 50 - 1 - suffix.Length;
        if (allowedBaseLength <= 0)
        {
            return $"t-{suffix}";
        }

        if (baseCode.Length > allowedBaseLength)
        {
            baseCode = baseCode[..allowedBaseLength];
        }

        return $"{baseCode}-{suffix}";
    }

    private static string BuildTenantCodeBase(string tenantName)
    {
        var builder = new StringBuilder();
        var lastDash = false;
        foreach (var ch in tenantName.Trim())
        {
            if (ch <= 127 && char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (builder.Length > 0 && !lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        var baseCode = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(baseCode) ? "tenant" : baseCode;
    }
}
