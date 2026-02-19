namespace WattsOn.Infrastructure.DataHub;

/// <summary>
/// Maps RSM document types to DataHub B2B API endpoint paths.
/// Based on: https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/654147782
/// </summary>
public static class DataHubEndpoints
{
    /// <summary>
    /// RSM document type → POST endpoint path (relative to base URL).
    /// Only includes document types that have a POST endpoint (outbound from supplier).
    /// </summary>
    private static readonly Dictionary<string, string> EndpointMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Phase 3 — Masterdata (BRS processes)
        ["RSM-001"] = "/requestchangeofsupplier",
        ["RSM-005"] = "/requestendofsupply",
        ["RSM-006"] = "/requestaccountingpointcharacteristics",
        ["RSM-020"] = "/requestservice",
        ["RSM-021"] = "/requestchangeaccountingpointcharacteristics",

        // Phase 2 — Metered Data
        ["RSM-012"] = "/notifyvalidatedmeasuredata",
        ["RSM-015"] = "/requestvalidatedmeasurements",

        // Phase 1 — Aggregations & Wholesale
        ["RSM-016"] = "/requestaggregatedmeasuredata",
        ["RSM-017"] = "/requestwholesalesettlement",

        // RSM-027 (customer master data update) uses /requestservice per DataHub mapping
        ["RSM-027"] = "/requestservice",

        // BRS-034 — Request prices
        ["RSM-035"] = "/requestservice",

        // BRS-038 — Request charge links
        ["RSM-032"] = "/requestservice",
    };

    /// <summary>
    /// Get the DataHub POST endpoint for a document type.
    /// Returns null if the document type has no POST endpoint (outbound-only from DataHub).
    /// </summary>
    public static string? GetEndpoint(string documentType)
    {
        return EndpointMap.TryGetValue(documentType, out var endpoint) ? endpoint : null;
    }

    /// <summary>All known document types with POST endpoints.</summary>
    public static IReadOnlyCollection<string> SupportedDocumentTypes => EndpointMap.Keys;
}
