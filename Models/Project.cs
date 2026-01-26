using SqlSugar;

namespace EasyRecordWorkingApi.Models;

[SugarTable("projects")]
public class Project : TenantEntity
{
    [SugarColumn(Length = 100, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? Code { get; set; }

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Status { get; set; } = "active";

    [SugarColumn(IsNullable = true)]
    public DateOnly? PlannedStartDate { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateOnly? PlannedEndDate { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Remark { get; set; }
}
