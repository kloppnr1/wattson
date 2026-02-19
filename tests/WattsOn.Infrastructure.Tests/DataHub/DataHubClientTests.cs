using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WattsOn.Infrastructure.DataHub;

namespace WattsOn.Infrastructure.Tests.DataHub;

/// <summary>
/// Tests for DataHubClient simulation mode and result types.
/// No Docker or database required.
/// </summary>
public class DataHubClientTests
{
    private static DataHubClient CreateSimulationClient()
    {
        var settings = new DataHubSettings
        {
            ClientId = "",       // Empty = simulation mode
            ClientSecret = "",
            BaseUrl = "https://preprod.b2b.datahub3.dk/v1.0/cim"
        };
        return new DataHubClient(
            new HttpClient(),
            Options.Create(settings),
            NullLogger<DataHubClient>.Instance);
    }

    [Fact]
    public void IsSimulationMode_NoCredentials_True()
    {
        var client = CreateSimulationClient();
        Assert.True(client.IsSimulationMode);
    }

    [Fact]
    public void IsSimulationMode_WithCredentials_False()
    {
        var settings = new DataHubSettings
        {
            ClientId = "some-client-id",
            ClientSecret = "some-secret"
        };
        var client = new DataHubClient(
            new HttpClient(),
            Options.Create(settings),
            NullLogger<DataHubClient>.Instance);

        Assert.False(client.IsSimulationMode);
    }

    [Fact]
    public async Task SendAsync_Simulation_RSM005_Accepted()
    {
        var client = CreateSimulationClient();
        var payload = "{\"businessReason\":\"E03\",\"gsrn\":\"571313180000000005\"}";

        var result = await client.SendAsync("RSM-005", payload);

        Assert.Equal(DataHubSendStatus.Accepted, result.Status);
        Assert.NotNull(result.ResponseBody);
        Assert.Contains("simulation", result.ResponseBody);
        Assert.Contains("requestendofsupply", result.ResponseBody);
    }

    [Fact]
    public async Task SendAsync_Simulation_RSM027_Accepted()
    {
        var client = CreateSimulationClient();

        var result = await client.SendAsync("RSM-027", "{\"businessReason\":\"E34\"}");

        Assert.Equal(DataHubSendStatus.Accepted, result.Status);
        Assert.Contains("requestservice", result.ResponseBody!);
    }

    [Fact]
    public async Task SendAsync_UnmappedDocType_Rejected()
    {
        var client = CreateSimulationClient();

        var result = await client.SendAsync("RSM-999", "{}");

        Assert.Equal(DataHubSendStatus.Rejected, result.Status);
        Assert.Contains("No POST endpoint mapped", result.Error);
    }

    [Theory]
    [InlineData("RSM-001", "/requestchangeofsupplier")]     // BRS-001/003
    [InlineData("RSM-005", "/requestendofsupply")]           // BRS-002/010/011
    [InlineData("RSM-016", "/requestaggregatedmeasuredata")] // BRS-023 request
    [InlineData("RSM-017", "/requestwholesalesettlement")]   // BRS-027 request
    [InlineData("RSM-027", "/requestservice")]               // BRS-015
    [InlineData("RSM-032", "/requestservice")]               // BRS-038
    [InlineData("RSM-035", "/requestservice")]               // BRS-034
    public async Task SendAsync_Simulation_AllOutboundTypes_Accepted(string docType, string expectedEndpoint)
    {
        var client = CreateSimulationClient();
        var result = await client.SendAsync(docType, "{\"test\":true}");
        Assert.Equal(DataHubSendStatus.Accepted, result.Status);
        Assert.NotNull(result.ResponseBody);
        Assert.Contains(expectedEndpoint, result.ResponseBody);
    }

    [Fact]
    public void SendResult_Accepted_Properties()
    {
        var result = DataHubSendResult.Accepted("{\"status\":\"ok\"}");
        Assert.Equal(DataHubSendStatus.Accepted, result.Status);
        Assert.Equal("{\"status\":\"ok\"}", result.ResponseBody);
        Assert.Null(result.Error);
    }

    [Fact]
    public void SendResult_Rejected_Properties()
    {
        var result = DataHubSendResult.Rejected("Bad payload");
        Assert.Equal(DataHubSendStatus.Rejected, result.Status);
        Assert.Null(result.ResponseBody);
        Assert.Equal("Bad payload", result.Error);
    }

    [Fact]
    public void SendResult_TransientFailure_Properties()
    {
        var result = DataHubSendResult.TransientFailure("503 maintenance");
        Assert.Equal(DataHubSendStatus.TransientFailure, result.Status);
        Assert.Null(result.ResponseBody);
        Assert.Equal("503 maintenance", result.Error);
    }

    [Fact]
    public void Settings_Defaults_ArePreprod()
    {
        var settings = new DataHubSettings();
        Assert.Contains("preprod", settings.BaseUrl);
        Assert.Equal("20e7a6b4-86e0-4e7a-a34d-6dc5a75d1982", settings.TenantId);
        Assert.Equal(5, settings.MaxRetries);
        Assert.Equal(30, settings.RetryBaseDelaySeconds);
        Assert.Equal(15, settings.PollIntervalSeconds);
        Assert.Equal(10, settings.BatchSize);
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void Settings_IsConfigured_RequiresBoth()
    {
        var s1 = new DataHubSettings { ClientId = "id", ClientSecret = "" };
        Assert.False(s1.IsConfigured);

        var s2 = new DataHubSettings { ClientId = "", ClientSecret = "secret" };
        Assert.False(s2.IsConfigured);

        var s3 = new DataHubSettings { ClientId = "id", ClientSecret = "secret" };
        Assert.True(s3.IsConfigured);
    }
}
