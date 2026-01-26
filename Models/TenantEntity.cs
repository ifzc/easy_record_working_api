using SqlSugar;

namespace EasyRecordWorkingApi.Models;

public abstract class TenantEntity : BaseEntity
{
    [SugarColumn(IsNullable = false)]
    public Guid TenantId { get; set; }
}
