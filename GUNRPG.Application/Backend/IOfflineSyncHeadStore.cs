namespace GUNRPG.Application.Backend;

public interface IOfflineSyncHeadStore
{
    Task<OfflineSyncHead?> GetAsync(Guid operatorId);
    Task UpsertAsync(OfflineSyncHead head);
}
