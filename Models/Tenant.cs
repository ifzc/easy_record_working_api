namespace EasyRecordWorkingApi.Models;

public class Tenant : BaseEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = "active";
}
