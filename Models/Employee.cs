namespace EasyRecordWorkingApi.Models;

public class Employee : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = "正式工";

    public bool IsActive { get; set; } = true;

    public string? Remark { get; set; }

    public Tenant? Tenant { get; set; }
}
