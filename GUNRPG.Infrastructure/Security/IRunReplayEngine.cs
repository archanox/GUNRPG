namespace GUNRPG.Security;

public interface IRunReplayEngine
{
    RunValidationResult Replay(RunInput input);
}
