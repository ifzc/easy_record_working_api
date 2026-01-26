using EasyRecordWorkingApi.Models;
using SqlSugar;

namespace EasyRecordWorkingApi.Data;

public static class SqlSugarExtensions
{
    public static async Task<int> InsertWithTimestampAsync<T>(this ISqlSugarClient db, T entity) where T : BaseEntity, new()
    {
        var now = DateTime.Now;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        return await db.Insertable(entity).ExecuteCommandAsync();
    }

    public static async Task<int> InsertRangeWithTimestampAsync<T>(this ISqlSugarClient db, List<T> entities) where T : BaseEntity, new()
    {
        var now = DateTime.Now;
        foreach (var entity in entities)
        {
            entity.CreatedAt = now;
            entity.UpdatedAt = now;
        }
        return await db.Insertable(entities).ExecuteCommandAsync();
    }

    public static async Task<int> UpdateWithTimestampAsync<T>(this ISqlSugarClient db, T entity) where T : BaseEntity, new()
    {
        entity.UpdatedAt = DateTime.Now;
        return await db.Updateable(entity).ExecuteCommandAsync();
    }
}
