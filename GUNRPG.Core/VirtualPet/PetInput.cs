namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Abstract record representing an action applied to a PetState.
/// Pure data model with no behavior or logic.
/// </summary>
public abstract record PetInput;

/// <summary>
/// Input representing rest/sleep action.
/// </summary>
/// <param name="Duration">Duration of the rest period.</param>
public sealed record RestInput(TimeSpan Duration) : PetInput;

/// <summary>
/// Input representing eating action.
/// </summary>
/// <param name="Nutrition">Nutrition value provided by the food.</param>
public sealed record EatInput(float Nutrition) : PetInput;

/// <summary>
/// Input representing drinking action.
/// </summary>
/// <param name="Hydration">Hydration value provided by the drink.</param>
public sealed record DrinkInput(float Hydration) : PetInput;

/// <summary>
/// Input representing mission/combat deployment action.
/// </summary>
/// <param name="HitsTaken">Number of hits taken during the mission.</param>
/// <param name="OpponentDifficulty">Difficulty rating of the opponent (0-100).</param>
public sealed record MissionInput(int HitsTaken, float OpponentDifficulty) : PetInput;
