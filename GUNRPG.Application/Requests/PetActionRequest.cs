namespace GUNRPG.Application.Requests;

public sealed class PetActionRequest
{
    /// <summary>
    /// Action type: rest, eat, drink, or mission.
    /// </summary>
    public string Action { get; set; } = "rest";
    public float? Hours { get; set; }
    public float? Nutrition { get; set; }
    public float? Hydration { get; set; }
    public int? HitsTaken { get; set; }
    public float? OpponentDifficulty { get; set; }
}
