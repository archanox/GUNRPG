namespace GUNRPG.Api.Dtos;

/// <summary>
/// API-specific intent DTO, decoupled from domain types.
/// </summary>
public sealed class ApiIntentDto
{
    public string? Primary { get; set; }
    public string? Movement { get; set; }
    public string? Stance { get; set; }
    public string? Cover { get; set; }
    public bool CancelMovement { get; set; }
}
