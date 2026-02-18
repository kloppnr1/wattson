using System.Text.RegularExpressions;
using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// Danish CVR number — 8-digit company registration number.
/// </summary>
public partial class CvrNumber : ValueObject
{
    public string Value { get; }

    private CvrNumber(string value) => Value = value;

    public static CvrNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("CVR number cannot be empty.", nameof(value));

        value = value.Trim();

        if (!CvrRegex().IsMatch(value))
            throw new ArgumentException($"CVR number must be exactly 8 digits. Got: '{value}'", nameof(value));

        return new CvrNumber(value);
    }

    public static CvrNumber FromTrusted(string value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex CvrRegex();
}
