using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WattsOn.Infrastructure.DataHub;

/// <summary>
/// HTTP client for DataHub 3.0 B2B API.
/// Handles OAuth2 client credentials auth, token caching, and CIM JSON posting.
/// When credentials are not configured, operates in simulation mode (logs what would be sent).
/// </summary>
public class DataHubClient
{
    private readonly HttpClient _httpClient;
    private readonly DataHubSettings _settings;
    private readonly ILogger<DataHubClient> _logger;

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public DataHubClient(HttpClient httpClient, IOptions<DataHubSettings> settings, ILogger<DataHubClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Whether real DataHub communication is enabled (credentials configured).</summary>
    public bool IsSimulationMode => !_settings.IsConfigured;

    /// <summary>
    /// Peek the next message from a DataHub queue.
    /// Returns null if queue is empty (204/404) or on error.
    /// </summary>
    public async Task<DataHubPeekResult?> PeekAsync(string queueEndpoint, CancellationToken ct = default)
    {
        if (IsSimulationMode) return null; // No messages in simulation

        var token = await GetTokenAsync(ct);
        var fullUrl = $"{_settings.BaseUrl}{queueEndpoint}";

        using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error peeking {Queue}", queueEndpoint);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout peeking {Queue}", queueEndpoint);
            return null;
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null; // Queue empty

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Peek {Queue} failed: {Status}", queueEndpoint, response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        // Extract message ID from response header or CIM document mRID
        var messageId = response.Headers.TryGetValues("MessageId", out var values)
            ? values.FirstOrDefault()
            : ExtractDocumentMrid(body);

        return new DataHubPeekResult(messageId ?? Guid.NewGuid().ToString(), body);
    }

    /// <summary>
    /// Dequeue (acknowledge) a message from DataHub.
    /// </summary>
    public async Task<bool> DequeueAsync(string messageId, CancellationToken ct = default)
    {
        if (IsSimulationMode) return true;

        var token = await GetTokenAsync(ct);
        var fullUrl = $"{_settings.BaseUrl}/dequeue/{messageId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, fullUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error dequeuing message {MessageId}", messageId);
            return false;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout dequeuing message {MessageId}", messageId);
            return false;
        }

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Send a CIM JSON message to DataHub.
    /// Returns a result indicating success, rejection, or transient failure.
    /// </summary>
    public async Task<DataHubSendResult> SendAsync(string documentType, string payload, CancellationToken ct = default)
    {
        var endpoint = DataHubEndpoints.GetEndpoint(documentType);
        if (endpoint is null)
        {
            return DataHubSendResult.Rejected($"No POST endpoint mapped for document type '{documentType}'");
        }

        if (IsSimulationMode)
        {
            return SimulateSend(documentType, endpoint, payload);
        }

        return await SendToDataHub(documentType, endpoint, payload, ct);
    }

    private DataHubSendResult SimulateSend(string documentType, string endpoint, string payload)
    {
        var fullUrl = $"{_settings.BaseUrl}{endpoint}";

        _logger.LogInformation(
            "[SIMULATION] Would POST {DocumentType} to {Url} ({PayloadLength} bytes)",
            documentType, fullUrl, payload.Length);

        // Parse payload to log a readable summary
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var summary = new StringBuilder();
            foreach (var prop in root.EnumerateObject().Take(5))
            {
                summary.Append($"{prop.Name}={prop.Value}, ");
            }
            _logger.LogDebug("[SIMULATION] Payload summary: {Summary}", summary.ToString().TrimEnd(',', ' '));
        }
        catch
        {
            // Not JSON or malformed — that's fine for simulation
        }

        return DataHubSendResult.Accepted($"{{\"simulation\":true,\"endpoint\":\"{fullUrl}\"}}");
    }

    private async Task<DataHubSendResult> SendToDataHub(string documentType, string endpoint, string payload, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var fullUrl = $"{_settings.BaseUrl}{endpoint}";

        using var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending {DocumentType} to DataHub: POST {Url}", documentType, fullUrl);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error sending {DocumentType} to DataHub", documentType);
            return DataHubSendResult.TransientFailure($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout sending {DocumentType} to DataHub", documentType);
            return DataHubSendResult.TransientFailure("Request timed out");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        return response.StatusCode switch
        {
            HttpStatusCode.OK or HttpStatusCode.Accepted =>
                DataHubSendResult.Accepted(responseBody),

            HttpStatusCode.BadRequest =>
                DataHubSendResult.Rejected($"400 Bad Request: {responseBody}"),

            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                HandleAuthFailure(responseBody),

            HttpStatusCode.ServiceUnavailable =>
                DataHubSendResult.TransientFailure($"503 Service Unavailable (maintenance): {responseBody}"),

            _ =>
                DataHubSendResult.TransientFailure($"Unexpected {(int)response.StatusCode}: {responseBody}")
        };
    }

    private DataHubSendResult HandleAuthFailure(string responseBody)
    {
        // Invalidate cached token so next attempt re-authenticates
        _cachedToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;
        _logger.LogWarning("DataHub auth failure — token invalidated. Response: {Body}", responseBody);
        return DataHubSendResult.TransientFailure($"Authentication failed: {responseBody}");
    }

    private static string? ExtractDocumentMrid(string cimJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(cimJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.EndsWith("_MarketDocument") && prop.Value.TryGetProperty("mRID", out var mrid))
                    return mrid.GetString();
            }
        }
        catch
        {
            // Not valid JSON or unexpected structure — fall through
        }
        return null;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // Return cached token if still valid (with 5 min buffer)
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            {
                return _cachedToken;
            }

            var tokenUrl = $"https://login.microsoftonline.com/{_settings.TenantId}/oauth2/v2.0/token";

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            tokenRequest.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _settings.ClientId!),
                new KeyValuePair<string, string>("client_secret", _settings.ClientSecret!),
                new KeyValuePair<string, string>("scope", _settings.Scope),
            });

            _logger.LogDebug("Requesting OAuth2 token from {Url}", tokenUrl);

            var response = await _httpClient.SendAsync(tokenRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to acquire DataHub token: {(int)response.StatusCode} {body}");
            }

            using var doc = JsonDocument.Parse(body);
            _cachedToken = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Token response missing access_token");

            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            _logger.LogInformation("DataHub OAuth2 token acquired, expires in {Seconds}s", expiresIn);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

/// <summary>Result of attempting to send a message to DataHub.</summary>
public record DataHubSendResult
{
    public DataHubSendStatus Status { get; init; }
    public string? ResponseBody { get; init; }
    public string? Error { get; init; }

    public static DataHubSendResult Accepted(string? responseBody = null) =>
        new() { Status = DataHubSendStatus.Accepted, ResponseBody = responseBody };

    public static DataHubSendResult Rejected(string error) =>
        new() { Status = DataHubSendStatus.Rejected, Error = error };

    public static DataHubSendResult TransientFailure(string error) =>
        new() { Status = DataHubSendStatus.TransientFailure, Error = error };
}

public enum DataHubSendStatus
{
    /// <summary>DataHub accepted the message (200/202)</summary>
    Accepted,

    /// <summary>DataHub rejected the message (400, bad payload) — don't retry</summary>
    Rejected,

    /// <summary>Transient failure (network, 503, auth) — retry with backoff</summary>
    TransientFailure
}

/// <summary>Result of peeking a message from a DataHub queue.</summary>
public record DataHubPeekResult(string MessageId, string Payload);
