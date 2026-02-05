using GUNRPG.Core.Intents;

namespace GUNRPG.Application.Dtos;

public sealed class IntentDto
{
    public PrimaryAction? Primary { get; set; }
    public MovementAction? Movement { get; set; }
    public StanceAction? Stance { get; set; }
    public CoverAction? Cover { get; set; }
    public bool CancelMovement { get; set; }
}
