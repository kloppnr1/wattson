using System.Text.Json;

namespace WattsOn.Infrastructure.DataHub;

/// <summary>
/// Classifies a CIM JSON envelope into BRS process type and RSM document type.
/// Used by DataHubInboxFetcher to create properly tagged InboxMessages.
/// </summary>
public static class CimMessageClassifier
{
    public record CimClassification(
        string? BusinessProcess,  // e.g., "BRS-001"
        string? DocumentType,     // e.g., "RSM-001"
        string? SenderGln,
        string? ReceiverGln,
        string? DocumentName      // e.g., "ConfirmRequestChangeOfSupplier_MarketDocument"
    );

    // ── Fallback: processType.value → BRS ───────────────────────────────
    private static readonly Dictionary<string, string> ProcessTypeToBrs = new(StringComparer.Ordinal)
    {
        ["E03"] = "BRS-001",
        ["E20"] = "BRS-002",
        ["E65"] = "BRS-009",
        ["E23"] = "BRS-021",
        ["D04"] = "BRS-023",
        ["D05"] = "BRS-027",
        ["D18"] = "BRS-031",
        ["D08"] = "BRS-031",
        ["D17"] = "BRS-037",
        ["D34"] = "BRS-003",
        ["D35"] = "BRS-003",
        ["D07"] = "BRS-003",
        ["E04"] = "BRS-004",
        ["E06"] = "BRS-006",
        ["E07"] = "BRS-007",
        ["E08"] = "BRS-008",
        ["E09"] = "BRS-013",
        ["E36"] = "BRS-036",
        ["E44"] = "BRS-044",
    };

    /// <summary>
    /// Classify a CIM JSON payload into BRS/RSM types, sender/receiver GLN.
    /// Returns a classification with nulls for any fields that could not be determined.
    /// Never throws — returns partial results on parse errors.
    /// </summary>
    public static CimClassification Classify(string? cimJson)
    {
        if (string.IsNullOrWhiteSpace(cimJson))
            return new CimClassification(null, null, null, null, null);

        JsonElement document;
        string? documentName;
        try
        {
            using var parsed = JsonDocument.Parse(cimJson);
            // Find the root _MarketDocument property
            document = default;
            documentName = null;
            foreach (var prop in parsed.RootElement.EnumerateObject())
            {
                if (prop.Name.EndsWith("_MarketDocument"))
                {
                    documentName = prop.Name;
                    // Clone so it survives JsonDocument disposal
                    document = prop.Value.Clone();
                    break;
                }
            }

            if (documentName is null)
                return new CimClassification(null, null, null, null, null);
        }
        catch (JsonException)
        {
            return new CimClassification(null, null, null, null, null);
        }

        // Extract sender/receiver GLN
        var senderGln = UnwrapValue(document, "sender_MarketParticipant.mRID");
        var receiverGln = UnwrapValue(document, "receiver_MarketParticipant.mRID");

        // Extract type code (A02 = rejection, E04 = new metering point, etc.)
        var typeCode = UnwrapValue(document, "type");
        var processType = UnwrapValue(document, "process.processType");

        // ── Classify by document name ───────────────────────────────────
        var (brs, rsm) = ClassifyByDocumentName(documentName, typeCode, processType);

        // ── Fallback by processType if document name didn't match ────────
        if (brs is null && processType is not null)
        {
            ProcessTypeToBrs.TryGetValue(processType, out brs);
            // Infer RSM from BRS if we got a fallback match
            rsm ??= InferRsmFromBrs(brs);
        }

        return new CimClassification(brs, rsm, senderGln, receiverGln, documentName);
    }

    private static (string? Brs, string? Rsm) ClassifyByDocumentName(
        string documentName, string? typeCode, string? processType)
    {
        // ChangeOfSupplier → BRS-001 / RSM-001
        if (documentName.Contains("ChangeOfSupplier", StringComparison.OrdinalIgnoreCase))
            return ("BRS-001", "RSM-001");

        // EndOfSupply — Notify vs Confirm/Reject
        if (documentName.Contains("EndOfSupply", StringComparison.OrdinalIgnoreCase))
        {
            if (documentName.Contains("Notify", StringComparison.OrdinalIgnoreCase))
                return ("BRS-001", "RSM-004"); // We're losing customer (notification)

            return ("BRS-002", "RSM-005"); // Our end-of-supply confirmed/rejected
        }

        // ValidatedMeasureData + Notify → BRS-021 / RSM-012
        if (documentName.Contains("ValidatedMeasureData", StringComparison.OrdinalIgnoreCase)
            && documentName.Contains("Notify", StringComparison.OrdinalIgnoreCase))
            return ("BRS-021", "RSM-012");

        // AggregatedMeasureData → BRS-023 / RSM-014
        if (documentName.Contains("AggregatedMeasureData", StringComparison.OrdinalIgnoreCase))
            return ("BRS-023", "RSM-014");

        // WholesaleSettlement or WholesaleServices → BRS-027 / RSM-019
        if (documentName.Contains("WholesaleSettlement", StringComparison.OrdinalIgnoreCase)
            || documentName.Contains("WholesaleServices", StringComparison.OrdinalIgnoreCase))
            return ("BRS-027", "RSM-019");

        // ChargeLinks → BRS-037 / RSM-033 (check before ChargeInformation)
        if (documentName.Contains("ChargeLinks", StringComparison.OrdinalIgnoreCase))
            return ("BRS-037", "RSM-033");

        // PriceList or ChargeInformation → BRS-031 / RSM-033
        if (documentName.Contains("PriceList", StringComparison.OrdinalIgnoreCase)
            || documentName.Contains("ChargeInformation", StringComparison.OrdinalIgnoreCase))
            return ("BRS-031", "RSM-033");

        // MeteringPoint — distinguish by type code
        if (documentName.Contains("MeteringPoint", StringComparison.OrdinalIgnoreCase))
        {
            if (typeCode == "E04" || processType == "E04")
                return ("BRS-004", "RSM-020"); // New metering point

            return ("BRS-006", "RSM-020"); // MP characteristics update
        }

        return (null, null);
    }

    /// <summary>
    /// When we know the BRS from processType fallback but not the RSM,
    /// infer a likely RSM from the BRS code.
    /// </summary>
    private static string? InferRsmFromBrs(string? brs) => brs switch
    {
        "BRS-001" => "RSM-001",
        "BRS-002" => "RSM-005",
        "BRS-003" => "RSM-001",
        "BRS-004" => "RSM-020",
        "BRS-006" => "RSM-020",
        "BRS-007" => "RSM-020",
        "BRS-008" => "RSM-020",
        "BRS-009" => "RSM-005",
        "BRS-013" => "RSM-005",
        "BRS-021" => "RSM-012",
        "BRS-023" => "RSM-014",
        "BRS-027" => "RSM-019",
        "BRS-031" => "RSM-033",
        "BRS-036" => "RSM-020",
        "BRS-037" => "RSM-033",
        "BRS-044" => "RSM-020",
        _ => null,
    };

    // ── Helpers (same unwrap logic as CimPayloadExtractor) ──────────────

    private static string? UnwrapValue(JsonElement parent, string propertyName)
    {
        // CIM JSON uses flat dotted property names (e.g., "process.processType" is a single key)
        if (!parent.TryGetProperty(propertyName, out var el))
            return null;
        return Unwrap(el);
    }

    private static string? Unwrap(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object when el.TryGetProperty("value", out var v) => Unwrap(v),
            _ => null,
        };
    }
}
