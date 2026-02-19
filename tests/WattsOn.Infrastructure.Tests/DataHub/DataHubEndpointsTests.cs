using WattsOn.Infrastructure.DataHub;

namespace WattsOn.Infrastructure.Tests.DataHub;

/// <summary>
/// Tests for DataHub endpoint mapping (RSM document type → API path).
/// No Docker or database required.
/// </summary>
public class DataHubEndpointsTests
{
    [Theory]
    [InlineData("RSM-001", "/requestchangeofsupplier")]
    [InlineData("RSM-005", "/requestendofsupply")]
    [InlineData("RSM-006", "/requestaccountingpointcharacteristics")]
    [InlineData("RSM-012", "/notifyvalidatedmeasuredata")]
    [InlineData("RSM-015", "/requestvalidatedmeasurements")]
    [InlineData("RSM-016", "/requestaggregatedmeasuredata")]
    [InlineData("RSM-017", "/requestwholesalesettlement")]
    [InlineData("RSM-020", "/requestservice")]
    [InlineData("RSM-021", "/requestchangeaccountingpointcharacteristics")]
    [InlineData("RSM-027", "/requestservice")]
    public void GetEndpoint_KnownDocumentType_ReturnsCorrectPath(string docType, string expected)
    {
        var result = DataHubEndpoints.GetEndpoint(docType);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("RSM-004")]  // Generic notification — outbound only from DataHub
    [InlineData("RSM-014")]  // Notify aggregated — outbound only
    [InlineData("RSM-019")]  // Notify wholesale — outbound only
    [InlineData("RSM-999")]  // Totally unknown
    [InlineData("")]
    public void GetEndpoint_UnknownOrOutboundOnly_ReturnsNull(string docType)
    {
        var result = DataHubEndpoints.GetEndpoint(docType);
        Assert.Null(result);
    }

    [Fact]
    public void GetEndpoint_IsCaseInsensitive()
    {
        Assert.Equal("/requestendofsupply", DataHubEndpoints.GetEndpoint("rsm-005"));
        Assert.Equal("/requestendofsupply", DataHubEndpoints.GetEndpoint("RSM-005"));
        Assert.Equal("/requestendofsupply", DataHubEndpoints.GetEndpoint("Rsm-005"));
    }

    [Fact]
    public void SupportedDocumentTypes_ContainsExpected()
    {
        var supported = DataHubEndpoints.SupportedDocumentTypes;
        Assert.Contains("RSM-001", supported);
        Assert.Contains("RSM-005", supported);
        Assert.Contains("RSM-027", supported);
        Assert.True(supported.Count >= 10, $"Expected at least 10 supported types, got {supported.Count}");
    }

    [Theory]
    [InlineData("RSM-001")]
    [InlineData("RSM-005")]
    [InlineData("RSM-016")]
    [InlineData("RSM-017")]
    [InlineData("RSM-027")]
    [InlineData("RSM-032")]
    [InlineData("RSM-035")]
    public void AllOutboundDocTypes_HaveEndpoints(string docType)
    {
        Assert.NotNull(DataHubEndpoints.GetEndpoint(docType));
    }
}
