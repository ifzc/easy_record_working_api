namespace EasyRecordWorkingApi.Models;

public class Project : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string Status { get; set; } = "active";

    public DateOnly? PlannedStartDate { get; set; }

    public DateOnly? PlannedEndDate { get; set; }

    public string? Remark { get; set; }

    public bool Deleted { get; set; }
}
