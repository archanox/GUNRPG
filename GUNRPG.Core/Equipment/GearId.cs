namespace GUNRPG.Core.Equipment;

/// <summary>
/// Strong-typed identifier for gear/equipment items.
/// Provides type safety and clear intent when working with equipment identities.
/// </summary>
public readonly struct GearId : IEquatable<GearId>
{
    private readonly string? _value;

    public string Value
    {
        get => _value ?? throw new InvalidOperationException("GearId is uninitialized. Use the constructor or FromString to create a valid instance before accessing Value.");
    }

    /// <summary>
    /// Indicates whether this GearId instance is uninitialized (i.e., default(GearId)).
    /// </summary>
    public bool IsEmpty => _value is null;

    public GearId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("GearId cannot be empty or whitespace", nameof(value));
        
        _value = value;
    }

    public static GearId FromString(string value) => new(value);

    public bool Equals(GearId other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is GearId other && Equals(other);

    public override int GetHashCode()
    {
        if (_value is null)
            throw new InvalidOperationException("Cannot compute a hash code for an uninitialized GearId (default value).");

        return _value.GetHashCode(StringComparison.Ordinal);
    }

    public override string ToString()
    {
        if (_value is null)
            throw new InvalidOperationException("Cannot convert an uninitialized GearId (default value) to string.");

        return _value;
    }

    public static bool operator ==(GearId left, GearId right) => left.Equals(right);

    public static bool operator !=(GearId left, GearId right) => !left.Equals(right);
}
