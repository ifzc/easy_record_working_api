using SqlSugar;

namespace EasyRecordWorkingApi.Models;

[SugarTable("tenants")]
public class Tenant : BaseEntity
{
    [SugarColumn(Length = 50, IsNullable = false)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Status { get; set; } = "active";
}
