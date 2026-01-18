namespace EasyRecordWorkingApi.Models;

public class Employee : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = "正式工";

    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
}
