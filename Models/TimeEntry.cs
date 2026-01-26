namespace EasyRecordWorkingApi.Models;

public class TimeEntry : TenantEntity
{
    public Guid EmployeeId { get; set; }

    public Guid? ProjectId { get; set; }

    public DateTime WorkDate { get; set; }

    public decimal NormalHours { get; set; } = 8;

    public decimal OvertimeHours { get; set; }

    public string? Remark { get; set; }

    public bool Deleted { get; set; }

    public Employee? Employee { get; set; }

    public Project? Project { get; set; }

    public Tenant? Tenant { get; set; }
}
