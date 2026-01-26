namespace EasyRecordWorkingApi.Models;

public class User : TenantEntity
{
    public string Account { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string Role { get; set; } = "member";

    public string Status { get; set; } = "active";
}
