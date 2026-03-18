using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Operators;

namespace GUNRPG.Ledger;

public sealed class LedgerGameStateProjector : IGameStateProjector
{
    public GameState Project(IEnumerable<RunLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var orderedEntries = entries
            .OrderBy(static entry => entry.Index)
            .ToArray();

        var operatorEvents = orderedEntries
            .SelectMany(static entry => entry.Run?.Mutation.OperatorEvents ?? [])
            .GroupBy(static evt => evt.OperatorId)
            .Select(static group => OperatorAggregate.FromEvents(group.OrderBy(evt => evt.SequenceNumber)))
            .OrderBy(static aggregate => aggregate.Id.Value)
            .ToArray();

        var runHistory = orderedEntries
            .Where(static entry => entry.Run is not null)
            .Select(static entry =>
            {
                var gameplayEvents = entry.Run!.Mutation.GameplayEvents;
                var completed = gameplayEvents.OfType<RunCompletedLedgerEvent>().LastOrDefault();
                return new GameState.RunHistoryEntry
                {
                    RunId = entry.Run.RunId,
                    PlayerId = entry.Run.PlayerId,
                    Timestamp = entry.Timestamp,
                    Outcome = completed?.Outcome ?? "Recorded",
                    Events = gameplayEvents.Select(evt => evt.EventType).ToArray()
                };
            })
            .ToArray();

        return new GameState
        {
            Players = operatorEvents.Select(ToPlayerState).ToArray(),
            RunHistory = runHistory
        };
    }

    private static GameState.PlayerState ToPlayerState(OperatorAggregate aggregate)
    {
        var inventory = new List<string>();
        if (!string.IsNullOrWhiteSpace(aggregate.EquippedWeaponName))
        {
            inventory.Add(aggregate.EquippedWeaponName);
        }

        if (!string.IsNullOrWhiteSpace(aggregate.LockedLoadout))
        {
            inventory.Add(aggregate.LockedLoadout);
        }

        return new GameState.PlayerState
        {
            PlayerId = aggregate.Id.Value,
            Name = aggregate.Name,
            TotalXp = aggregate.TotalXp,
            CurrentHealth = aggregate.CurrentHealth,
            MaxHealth = aggregate.MaxHealth,
            EquippedWeaponName = aggregate.EquippedWeaponName,
            Inventory = inventory,
            UnlockedPerks = aggregate.UnlockedPerks.ToArray(),
            ExfilStreak = aggregate.ExfilStreak,
            IsDead = aggregate.IsDead,
            CurrentMode = aggregate.CurrentMode.ToString(),
            InfilSessionId = aggregate.InfilSessionId,
            ActiveCombatSessionId = aggregate.ActiveCombatSessionId,
            LockedLoadout = aggregate.LockedLoadout,
            PetState = aggregate.PetState
        };
    }
}
