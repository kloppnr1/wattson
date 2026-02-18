using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.ValueObjects;

public class GsrnTests
{
    [Fact]
    public void Create_WithValid18Digits_Succeeds()
    {
        var gsrn = Gsrn.Create("571313180400013562");
        Assert.Equal("571313180400013562", gsrn.Value);
    }

    [Fact]
    public void Create_WithInvalidLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Gsrn.Create("57131318040001"));
    }

    [Fact]
    public void Create_WithNonDigits_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Gsrn.Create("57131318040001356X"));
    }

    [Fact]
    public void Create_WithEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Gsrn.Create(""));
        Assert.Throws<ArgumentException>(() => Gsrn.Create("   "));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var gsrn1 = Gsrn.Create("571313180400013562");
        var gsrn2 = Gsrn.Create("571313180400013562");
        Assert.Equal(gsrn1, gsrn2);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        var gsrn1 = Gsrn.Create("571313180400013562");
        var gsrn2 = Gsrn.Create("571313180400013579");
        Assert.NotEqual(gsrn1, gsrn2);
    }
}
