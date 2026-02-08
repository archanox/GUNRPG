namespace GUNRPG.Core.Equipment;

/// <summary>
/// Strong-typed identifier for gear/equipment items.
/// Provides type safety and clear intent when working with equipment identities.
/// </summary>
public readonly struct GearId : IEquatable<GearId>
{
    public string Value { get; }

    public GearId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("GearId cannot be empty or whitespace", nameof(value));
        
        Value = value;
    }

    public static GearId FromString(string value) => new(value);

    public bool Equals(GearId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is GearId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    public static bool operator ==(GearId left, GearId right) => left.Equals(right);

    public static bool operator !=(GearId left, GearId right) => !left.Equals(right);
}
