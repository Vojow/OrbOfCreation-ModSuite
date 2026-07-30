using System;

namespace OrbAutomata;

internal readonly struct ScrollRoleKey :
    IEquatable<ScrollRoleKey>,
    IComparable<ScrollRoleKey>
{
    internal ScrollRoleKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A Scroll role key is required.", nameof(value));
        Value = value;
    }

    internal string Value { get; }

    public bool Equals(ScrollRoleKey other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public int CompareTo(ScrollRoleKey other) =>
        string.Compare(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is ScrollRoleKey other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(ScrollRoleKey left, ScrollRoleKey right) =>
        left.Equals(right);

    public static bool operator !=(ScrollRoleKey left, ScrollRoleKey right) =>
        !left.Equals(right);
}
