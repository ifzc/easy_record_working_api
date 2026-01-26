namespace EasyRecordWorkingApi.Models;

public class Employee : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; }

    public string? WorkType { get; set; }

    public string? Phone { get; set; }

    public string? IdCardNumber { get; set; }

    public string? Remark { get; set; }

    public string? Tags { get; set; }

    public bool Deleted { get; set; }

    public Tenant? Tenant { get; set; }
}
