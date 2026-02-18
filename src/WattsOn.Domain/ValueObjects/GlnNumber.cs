using System.Text.RegularExpressions;
using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// Global Location Number — 13-digit identifier for market participants.
/// Used to identify actors (aktører) in the Danish electricity market.
/// </summary>
public partial class GlnNumber : ValueObject
{
    public string Value { get; }

    private GlnNumber(string value) => Value = value;

    public static GlnNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("GLN number cannot be empty.", nameof(value));

        value = value.Trim();

        if (!GlnRegex().IsMatch(value))
            throw new ArgumentException($"GLN number must be exactly 13 digits. Got: '{value}'", nameof(value));

        if (!IsValidCheckDigit(value))
            throw new ArgumentException($"GLN number has invalid check digit: '{value}'", nameof(value));

        return new GlnNumber(value);
    }

    /// <summary>
    /// Create without validation — use only for known-good values from DataHub.
    /// </summary>
    public static GlnNumber FromTrusted(string value) => new(value);

    private static bool IsValidCheckDigit(string gln)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = gln[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        var checkDigit = (10 - sum % 10) % 10;
        return checkDigit == gln[12] - '0';
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\d{13}$")]
    private static partial Regex GlnRegex();
}
