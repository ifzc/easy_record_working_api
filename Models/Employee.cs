using SqlSugar;

namespace EasyRecordWorkingApi.Models;

[SugarTable("employees")]
public class Employee : TenantEntity
{
    [SugarColumn(Length = 50, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 10, IsNullable = false)]
    public string Type { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? WorkType { get; set; }

    [SugarColumn(Length = 20, IsNullable = true)]
    public string? Phone { get; set; }

    [SugarColumn(Length = 30, IsNullable = true)]
    public string? IdCardNumber { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Remark { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? Tags { get; set; }
}
