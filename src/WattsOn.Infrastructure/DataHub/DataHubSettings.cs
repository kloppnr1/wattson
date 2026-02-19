namespace WattsOn.Infrastructure.DataHub;

/// <summary>
/// Configuration for DataHub 3.0 B2B API communication.
/// When ClientId/ClientSecret are empty, the system runs in simulation mode.
/// </summary>
public class DataHubSettings
{
    public const string SectionName = "DataHub";

    /// <summary>Base URL for DataHub B2B API (e.g., https://b2b.datahub3.dk/v1.0/cim)</summary>
    public string BaseUrl { get; set; } = "https://preprod.b2b.datahub3.dk/v1.0/cim";

    /// <summary>Azure AD tenant ID for OAuth2 token endpoint</summary>
    public string TenantId { get; set; } = "20e7a6b4-86e0-4e7a-a34d-6dc5a75d1982"; // Preprod default

    /// <summary>OAuth2 scope for DataHub API</summary>
    public string Scope { get; set; } = "65877f1b-1aef-42b3-adc7-3009608f27a3/.default"; // Preprod default

    /// <summary>OAuth2 client ID (from DataHub B2B adgang)</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth2 client secret</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Max send attempts before dead-lettering a message</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Base delay for exponential backoff (seconds)</summary>
    public int RetryBaseDelaySeconds { get; set; } = 30;

    /// <summary>Poll interval for outbox worker (seconds)</summary>
    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>Batch size per poll cycle</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>True when credentials are configured and real sends should happen</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
