namespace EasyRecordWorkingApi.Dtos;

public class LoginRequest
{
    public string Account { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public LoginUser User { get; set; } = new();
    public LoginTenant Tenant { get; set; } = new();
}

public class LoginUser
{
    public Guid Id { get; set; }
    public string Account { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = string.Empty;
}

public class LoginTenant
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class CurrentUserResponse
{
    public LoginUser User { get; set; } = new();
    public LoginTenant Tenant { get; set; } = new();
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
