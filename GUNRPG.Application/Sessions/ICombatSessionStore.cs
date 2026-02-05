namespace GUNRPG.Application.Sessions;

public interface ICombatSessionStore
{
    CombatSession Create(CombatSession session);
    CombatSession? Get(Guid id);
    void Upsert(CombatSession session);
    IReadOnlyCollection<CombatSession> List();
}
