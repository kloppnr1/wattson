using System.Text.RegularExpressions;
using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// Global Service Relation Number — 18-digit identifier for metering points.
/// The primary key of the Danish electricity metering system.
/// Danish metering points start with 57 (DK country prefix).
/// </summary>
public partial class Gsrn : ValueObject
{
    public string Value { get; }

    private Gsrn(string value) => Value = value;

    public static Gsrn Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("GSRN cannot be empty.", nameof(value));

        value = value.Trim();

        if (!GsrnRegex().IsMatch(value))
            throw new ArgumentException($"GSRN must be exactly 18 digits. Got: '{value}'", nameof(value));

        return new Gsrn(value);
    }

    /// <summary>
    /// Create without validation — use only for known-good values from DataHub.
    /// </summary>
    public static Gsrn FromTrusted(string value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\d{18}$")]
    private static partial Regex GsrnRegex();
}
