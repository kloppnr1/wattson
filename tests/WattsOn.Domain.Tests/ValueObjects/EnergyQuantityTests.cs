using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.ValueObjects;

public class EnergyQuantityTests
{
    [Fact]
    public void Create_StoresValue()
    {
        var qty = EnergyQuantity.Create(1.234m);
        Assert.Equal(1.234m, qty.Value);
        Assert.Equal("kWh", qty.Unit);
    }

    [Fact]
    public void Create_RoundsToThreeDecimals()
    {
        var qty = EnergyQuantity.Create(1.23456m);
        Assert.Equal(1.235m, qty.Value);
    }

    [Fact]
    public void Add_CombinesValues()
    {
        var a = EnergyQuantity.Create(1.5m);
        var b = EnergyQuantity.Create(2.3m);
        var result = a + b;

        Assert.Equal(3.8m, result.Value);
    }

    [Fact]
    public void Subtract_FindsDifference()
    {
        var a = EnergyQuantity.Create(5m);
        var b = EnergyQuantity.Create(2m);
        var result = a - b;

        Assert.Equal(3m, result.Value);
    }

    [Fact]
    public void Negate_FlipsSign()
    {
        var qty = EnergyQuantity.Create(5m);
        var negated = -qty;
        Assert.Equal(-5m, negated.Value);
    }

    [Fact]
    public void Zero_HasZeroValue()
    {
        Assert.Equal(0m, EnergyQuantity.Zero.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = EnergyQuantity.Create(1.5m);
        var b = EnergyQuantity.Create(1.5m);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToString_FormatsWithUnit()
    {
        var qty = EnergyQuantity.Create(1.5m);
        Assert.Equal("1.500 kWh", qty.ToString());
    }
}
