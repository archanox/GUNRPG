namespace GUNRPG.Application.Gameplay;

public sealed class GameplayLedgerOptions
{
    public bool MirrorLegacyWrites { get; init; } = true;

    public bool PreferLedgerReads { get; init; }

    public bool CompareLegacyAndLedgerState { get; init; } = true;

    public bool RequireLedgerWrites { get; init; }
}
