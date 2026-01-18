namespace EasyRecordWorkingApi.Models;

public abstract class TenantEntity : BaseEntity
{
    public Guid TenantId { get; set; }
}
