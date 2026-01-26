using SqlSugar;

namespace EasyRecordWorkingApi.Models;

[SugarTable("users")]
public class User : TenantEntity
{
    [SugarColumn(Length = 100, IsNullable = false)]
    public string Account { get; set; } = string.Empty;

    [SugarColumn(Length = 255, IsNullable = false)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? DisplayName { get; set; }

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Role { get; set; } = "member";

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Status { get; set; } = "active";
}
