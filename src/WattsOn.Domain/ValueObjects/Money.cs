using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// Monetary amount with currency. Danish market uses DKK.
/// Rounded to 2 decimal places (øre precision).
/// </summary>
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money DKK(decimal amount) => new(Math.Round(amount, 2), "DKK");
    public static Money Create(decimal amount, string currency = "DKK") => new(Math.Round(amount, 2), currency);
    public static Money Zero => DKK(0m);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Negate() => new(-Amount, Currency);

    public Money Multiply(decimal factor) => new(Math.Round(Amount * factor, 2), Currency);

    public static Money operator +(Money a, Money b) => a.Add(b);
    public static Money operator -(Money a, Money b) => a.Subtract(b);
    public static Money operator -(Money a) => a.Negate();
    public static Money operator *(Money a, decimal factor) => a.Multiply(factor);

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot combine {Currency} and {other.Currency}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
