using GUNRPG.Application.Gameplay;

namespace GUNRPG.Ledger;

public interface IGameStateProjector
{
    GameState Project(IEnumerable<RunLedgerEntry> entries);
}
