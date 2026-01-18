using System.Security.Claims;

namespace EasyRecordWorkingApi.Services;

public interface IUserContext
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string? Account { get; }
    string? Role { get; }
}

public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId => GetGuidClaim(ClaimTypes.NameIdentifier) ?? GetGuidClaim("sub");

    public Guid? TenantId => GetGuidClaim("tenant_id");

    public string? Account => GetStringClaim("account");

    public string? Role => GetStringClaim(ClaimTypes.Role);

    private Guid? GetGuidClaim(string type)
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirstValue(type);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private string? GetStringClaim(string type)
    {
        return _httpContextAccessor.HttpContext?.User.FindFirstValue(type);
    }
}
