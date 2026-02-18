using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.ValueObjects;

public class GlnNumberTests
{
    [Fact]
    public void Create_WithValidGln_Succeeds()
    {
        // Energinet's GLN
        var gln = GlnNumber.Create("5790000432752");
        Assert.Equal("5790000432752", gln.Value);
    }

    [Fact]
    public void Create_WithInvalidLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GlnNumber.Create("123"));
    }

    [Fact]
    public void Create_WithNonDigits_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GlnNumber.Create("579000043275a"));
    }

    [Fact]
    public void Create_WithInvalidCheckDigit_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GlnNumber.Create("5790000432753"));
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        var gln = GlnNumber.Create("  5790000432752  ");
        Assert.Equal("5790000432752", gln.Value);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var gln1 = GlnNumber.Create("5790000432752");
        var gln2 = GlnNumber.Create("5790000432752");
        Assert.Equal(gln1, gln2);
    }

    [Fact]
    public void FromTrusted_SkipsValidation()
    {
        // Should not throw even with invalid check digit
        var gln = GlnNumber.FromTrusted("5790000432753");
        Assert.Equal("5790000432753", gln.Value);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var gln = GlnNumber.Create("5790000432752");
        Assert.Equal("5790000432752", gln.ToString());
    }
}
