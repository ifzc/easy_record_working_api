using SqlSugar;

namespace EasyRecordWorkingApi.Models;

public abstract class BaseEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool Deleted { get; set; }
}
