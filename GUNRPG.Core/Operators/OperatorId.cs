namespace GUNRPG.Core.Operators;

/// <summary>
/// Strong-typed identifier for an Operator.
/// Provides type safety and clear intent when working with operator identities.
/// </summary>
public readonly struct OperatorId : IEquatable<OperatorId>
{
    public Guid Value { get; }

    public OperatorId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("OperatorId cannot be empty", nameof(value));
        
        Value = value;
    }

    public static OperatorId NewId() => new(Guid.NewGuid());

    public static OperatorId FromGuid(Guid value) => new(value);

    public bool Equals(OperatorId other) => Value.Equals(other.Value);

    public override bool Equals(object? obj) => obj is OperatorId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public static bool operator ==(OperatorId left, OperatorId right) => left.Equals(right);

    public static bool operator !=(OperatorId left, OperatorId right) => !left.Equals(right);
}
