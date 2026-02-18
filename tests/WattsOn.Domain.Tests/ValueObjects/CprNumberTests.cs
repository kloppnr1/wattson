using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.ValueObjects;

public class CprNumberTests
{
    [Fact]
    public void Create_WithValidCpr_Succeeds()
    {
        var cpr = CprNumber.Create("0101901234");
        Assert.Equal("0101901234", cpr.Value);
    }

    [Fact]
    public void Create_WithDash_RemovesDash()
    {
        var cpr = CprNumber.Create("010190-1234");
        Assert.Equal("0101901234", cpr.Value);
    }

    [Fact]
    public void Create_WithInvalidLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CprNumber.Create("12345"));
    }

    [Fact]
    public void ToMasked_MaskesLastFourDigits()
    {
        var cpr = CprNumber.Create("0101901234");
        Assert.Equal("010190-****", cpr.ToMasked());
    }

    [Fact]
    public void ToString_ReturnsMasked_NeverFull()
    {
        var cpr = CprNumber.Create("0101901234");
        Assert.Equal("010190-****", cpr.ToString());
        Assert.DoesNotContain("1234", cpr.ToString());
    }
}
