using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void DKK_CreatesWithCorrectCurrency()
    {
        var money = Money.DKK(100.50m);
        Assert.Equal(100.50m, money.Amount);
        Assert.Equal("DKK", money.Currency);
    }

    [Fact]
    public void DKK_RoundsToTwoDecimals()
    {
        var money = Money.DKK(100.555m);
        Assert.Equal(100.56m, money.Amount);
    }

    [Fact]
    public void Add_SameCurrency_Succeeds()
    {
        var a = Money.DKK(100m);
        var b = Money.DKK(50.25m);
        var result = a + b;

        Assert.Equal(150.25m, result.Amount);
        Assert.Equal("DKK", result.Currency);
    }

    [Fact]
    public void Subtract_SameCurrency_Succeeds()
    {
        var a = Money.DKK(100m);
        var b = Money.DKK(30m);
        var result = a - b;

        Assert.Equal(70m, result.Amount);
    }

    [Fact]
    public void Add_DifferentCurrency_ThrowsInvalidOperationException()
    {
        var dkk = Money.DKK(100m);
        var eur = Money.Create(100m, "EUR");
        Assert.Throws<InvalidOperationException>(() => dkk + eur);
    }

    [Fact]
    public void Multiply_CalculatesCorrectly()
    {
        var money = Money.DKK(100m);
        var result = money * 0.25m;

        Assert.Equal(25m, result.Amount);
    }

    [Fact]
    public void Negate_FlipsSign()
    {
        var money = Money.DKK(100m);
        var negated = -money;

        Assert.Equal(-100m, negated.Amount);
    }

    [Fact]
    public void Zero_IsZeroDKK()
    {
        Assert.Equal(0m, Money.Zero.Amount);
        Assert.Equal("DKK", Money.Zero.Currency);
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        var a = Money.DKK(42.50m);
        var b = Money.DKK(42.50m);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToString_ContainsAmountAndCurrency()
    {
        var money = Money.DKK(42.50m);
        var str = money.ToString();
        Assert.Contains("DKK", str);
        Assert.Contains("42", str);
    }
}
