namespace EasyRecordWorkingApi.Models;

public class TimeEntry : TenantEntity
{
    public Guid EmployeeId { get; set; }

    public DateTime WorkDate { get; set; }

    public decimal NormalHours { get; set; } = 8;

    public decimal OvertimeHours { get; set; }

    public string? Remark { get; set; }

    public Employee? Employee { get; set; }

    public Tenant? Tenant { get; set; }
}
