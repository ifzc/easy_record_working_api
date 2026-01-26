using SqlSugar;

namespace EasyRecordWorkingApi.Models;

[SugarTable("time_entries")]
public class TimeEntry : TenantEntity
{
    [SugarColumn(IsNullable = false)]
    public Guid EmployeeId { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? ProjectId { get; set; }

    [SugarColumn(ColumnDataType = "date", IsNullable = false)]
    public DateTime WorkDate { get; set; }

    [SugarColumn(DecimalDigits = 2, Length = 5, IsNullable = false)]
    public decimal NormalHours { get; set; } = 8;

    [SugarColumn(DecimalDigits = 2, Length = 5, IsNullable = false)]
    public decimal OvertimeHours { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Remark { get; set; }
}
