using System.Text.RegularExpressions;
using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// Danish CPR number — 10-digit personal identification number.
/// Format: DDMMYY-XXXX
/// ⚠️ Sensitive personal data — handle with care (GDPR).
/// </summary>
public partial class CprNumber : ValueObject
{
    public string Value { get; }

    private CprNumber(string value) => Value = value;

    public static CprNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("CPR number cannot be empty.", nameof(value));

        // Remove dash if present (both DDMMYYXXXX and DDMMYY-XXXX accepted)
        value = value.Trim().Replace("-", "");

        if (!CprRegex().IsMatch(value))
            throw new ArgumentException("CPR number must be exactly 10 digits.", nameof(value));

        return new CprNumber(value);
    }

    public static CprNumber FromTrusted(string value) => new(value.Replace("-", ""));

    /// <summary>Returns masked format for display: DDMMYY-****</summary>
    public string ToMasked() => $"{Value[..6]}-****";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => ToMasked(); // Never accidentally log the full CPR

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex CprRegex();
}
