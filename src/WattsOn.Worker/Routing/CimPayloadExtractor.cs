using System.Text.Json;

namespace WattsOn.Worker.Routing;

/// <summary>
/// Auto-detects CIM JSON envelopes (root property ending with _MarketDocument)
/// and flattens them into the flat field format that existing inbox handlers expect.
/// Non-CIM payloads pass through unchanged.
/// </summary>
internal static class CimPayloadExtractor
{
    public static JsonElement ExtractPayload(string? rawPayload, string? businessProcess = null, string? documentType = null)
    {
        if (string.IsNullOrEmpty(rawPayload)) return default;

        var root = JsonSerializer.Deserialize<JsonElement>(rawPayload);
        if (root.ValueKind != JsonValueKind.Object) return root;

        // Detect CIM envelope: root property ending with _MarketDocument
        JsonElement? document = null;
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.EndsWith("_MarketDocument"))
            {
                document = prop.Value;
                break;
            }
        }

        if (document is null)
            return root; // Not CIM — passthrough

        var doc = document.Value;
        var flat = new Dictionary<string, object?>();

        // ── Document-level extractions ──────────────────────────────────────

        var typeCode = UnwrapString(doc, "type");
        var processType = UnwrapString(doc, "process.processType");
        var senderGln = UnwrapString(doc, "sender_MarketParticipant.mRID");

        // A02 = rejection
        if (typeCode == "A02")
            flat["rejected"] = true;

        // Business reason from document type code (D18/D08/D17) or process type (D34/D35/D07)
        if (typeCode is "D18" or "D08" or "D17")
            flat["businessReason"] = typeCode;
        else if (processType is "D34" or "D35" or "D07")
            flat["businessReason"] = processType;

        // Store sender GLN as internal key (for ownerGln fallback)
        flat["_senderGln"] = senderGln;

        // ── MktActivityRecord-based extraction ──────────────────────────────

        if (doc.TryGetProperty("MktActivityRecord", out var mktArr) && mktArr.ValueKind == JsonValueKind.Array)
        {
            var records = mktArr.EnumerateArray();
            if (records.Any())
            {
                var rec = mktArr[0];
                ExtractMktActivityRecord(rec, flat);
            }
        }

        // ── Series-based extraction ─────────────────────────────────────────

        if (doc.TryGetProperty("Series", out var seriesArr) && seriesArr.ValueKind == JsonValueKind.Array)
        {
            var items = seriesArr.EnumerateArray();
            if (items.Any())
            {
                var series = seriesArr[0];
                ExtractSeries(series, flat, businessProcess, documentType);
            }
        }

        // ── ownerGln fallback to sender GLN ─────────────────────────────────

        if (!flat.ContainsKey("ownerGln") && senderGln != null)
            flat["ownerGln"] = senderGln;

        // ── Clean internal keys ─────────────────────────────────────────────

        var internalKeys = flat.Keys.Where(k => k.StartsWith("_")).ToList();
        foreach (var key in internalKeys)
            flat.Remove(key);

        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(flat));
    }

    // ────────────────────────────────────────────────────────────────────────
    // MktActivityRecord field extraction
    // ────────────────────────────────────────────────────────────────────────

    private static void ExtractMktActivityRecord(JsonElement rec, Dictionary<string, object?> flat)
    {
        // transactionId
        SetIfPresent(flat, "transactionId", GetPlainString(rec, "mRID"));

        // gsrn (unwrap value)
        SetIfPresent(flat, "gsrn", UnwrapString(rec, "marketEvaluationPoint.mRID"));

        // effectiveDate + linkStart (same CIM field, both needed)
        var startDateTime = GetPlainString(rec, "start_DateAndOrTime.dateTime");
        SetIfPresent(flat, "effectiveDate", startDateTime);
        SetIfPresent(flat, "linkStart", startDateTime);

        // stopDate + linkEnd
        var endDateTime = GetPlainString(rec, "end_DateAndOrTime.dateTime");
        SetIfPresent(flat, "stopDate", endDateTime);
        SetIfPresent(flat, "linkEnd", endDateTime);

        // Customer info
        SetIfPresent(flat, "customerIdentifier", UnwrapString(rec, "customer_MarketParticipant.mRID"));
        SetIfPresent(flat, "customerName", GetPlainString(rec, "customer_MarketParticipant.name"));

        // New supplier (RSM-004 / lose customer)
        SetIfPresent(flat, "newSupplierGln", UnwrapString(rec, "energySupplier_MarketParticipant.mRID"));
        // Also check in_MarketParticipant.mRID (used in some CIM variants for the incoming supplier)
        if (!flat.ContainsKey("newSupplierGln"))
            SetIfPresent(flat, "newSupplierGln", UnwrapString(rec, "in_MarketParticipant.mRID"));

        // Original transaction
        SetIfPresent(flat, "originalTransactionId", GetPlainString(rec, "originalTransactionIDReference_MktActivityRecord.mRID"));

        // Charge info (D18 — lower case chargeType)
        SetIfPresent(flat, "chargeId", GetPlainString(rec, "chargeType.mRID"));
        SetIfPresent(flat, "priceType", UnwrapString(rec, "chargeType.type"));
        SetIfPresent(flat, "description", GetPlainString(rec, "chargeType.name"));
        if (rec.TryGetProperty("chargeType.VATexempt", out var vatEl))
            flat["vatExempt"] = vatEl.ValueKind == JsonValueKind.True;
        SetIfPresent(flat, "ownerGln", UnwrapString(rec, "chargeType.owner_MarketParticipant.mRID"));

        // Charge info (D17 — upper case ChargeType)
        if (!flat.ContainsKey("chargeId"))
            SetIfPresent(flat, "chargeId", GetPlainString(rec, "ChargeType.mRID"));
        if (!flat.ContainsKey("ownerGln"))
            SetIfPresent(flat, "ownerGln", UnwrapString(rec, "ChargeType.owner_MarketParticipant.mRID"));

        // Metering point fields (BRS-004/006)
        SetIfPresent(flat, "type", UnwrapString(rec, "marketEvaluationPoint.type"));
        SetIfPresent(flat, "art", UnwrapString(rec, "marketEvaluationPoint.meteringMethod"));
        SetIfPresent(flat, "settlementMethod", UnwrapString(rec, "marketEvaluationPoint.settlementMethod"));
        SetIfPresent(flat, "resolution", UnwrapString(rec, "marketEvaluationPoint.resolution"));
        SetIfPresent(flat, "connectionState", UnwrapString(rec, "marketEvaluationPoint.connectionState"));
        SetIfPresent(flat, "gridArea", UnwrapString(rec, "meteringGridArea_Domain.mRID"));
        SetIfPresent(flat, "gridCompanyGln", UnwrapString(rec, "gridCompany_MarketParticipant.mRID"));
        SetIfPresent(flat, "parentGsrn", UnwrapString(rec, "linked_MarketEvaluationPoint.mRID"));

        // Reason
        SetIfPresent(flat, "reason", GetPlainString(rec, "reason.text"));

        // BRS-003 fields
        SetIfPresent(flat, "currentSupplierGln", UnwrapString(rec, "currentSupplier_MarketParticipant.mRID"));
        SetIfPresent(flat, "erroneousSupplierGln", UnwrapString(rec, "erroneousSupplier_MarketParticipant.mRID"));
        SetIfPresent(flat, "resumeDate", GetPlainString(rec, "start_DateAndOrTime.dateTime"));

        // Address extraction (BRS-004/006)
        ExtractAddress(rec, flat);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Address extraction from usagePointLocation
    // ────────────────────────────────────────────────────────────────────────

    private static void ExtractAddress(JsonElement rec, Dictionary<string, object?> flat)
    {
        if (!rec.TryGetProperty("usagePointLocation.mainAddress.streetDetail.name", out _)
            && !rec.TryGetProperty("usagePointLocation.mainAddress.townDetail.name", out _))
            return;

        var addr = new Dictionary<string, object?>();

        SetIfPresent(addr, "streetName", GetPlainString(rec, "usagePointLocation.mainAddress.streetDetail.name"));
        SetIfPresent(addr, "buildingNumber", GetPlainString(rec, "usagePointLocation.mainAddress.streetDetail.number"));
        SetIfPresent(addr, "postCode", GetPlainString(rec, "usagePointLocation.mainAddress.townDetail.code"));
        SetIfPresent(addr, "cityName", GetPlainString(rec, "usagePointLocation.mainAddress.townDetail.name"));
        SetIfPresent(addr, "floor", GetPlainString(rec, "usagePointLocation.mainAddress.streetDetail.floorIdentification"));
        SetIfPresent(addr, "suite", GetPlainString(rec, "usagePointLocation.mainAddress.streetDetail.suiteNumber"));

        if (addr.Count > 0)
            flat["address"] = addr;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Series field extraction
    // ────────────────────────────────────────────────────────────────────────

    private static void ExtractSeries(JsonElement series, Dictionary<string, object?> flat,
        string? businessProcess, string? documentType)
    {
        // transactionId from series mRID
        SetIfPresent(flat, "transactionId", GetPlainString(series, "mRID"));

        // gsrn
        SetIfPresent(flat, "gsrn", UnwrapString(series, "marketEvaluationPoint.mRID"));

        // Metering point type
        SetIfPresent(flat, "meteringPointType", UnwrapString(series, "marketEvaluationPoint.type"));

        // Grid area
        SetIfPresent(flat, "gridArea", UnwrapString(series, "meteringGridArea_Domain.mRID"));

        // Charge info (for price series)
        SetIfPresent(flat, "chargeId", GetPlainString(series, "chargeType.mRID"));
        if (!flat.ContainsKey("ownerGln") || flat["ownerGln"] == null)
        {
            var ownerGln = UnwrapString(series, "chargeType.owner_MarketParticipant.mRID");
            if (ownerGln != null)
                flat["ownerGln"] = ownerGln;
        }

        // Period extraction
        ExtractPeriod(series, flat, businessProcess, documentType);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Period extraction (handles object or array)
    // ────────────────────────────────────────────────────────────────────────

    private static void ExtractPeriod(JsonElement series, Dictionary<string, object?> flat,
        string? businessProcess, string? documentType)
    {
        if (!series.TryGetProperty("Period", out var periodEl)) return;

        // Period can be object or array
        JsonElement period;
        if (periodEl.ValueKind == JsonValueKind.Array)
        {
            var items = periodEl.EnumerateArray();
            if (!items.Any()) return;
            period = periodEl[0];
        }
        else if (periodEl.ValueKind == JsonValueKind.Object)
        {
            period = periodEl;
        }
        else
        {
            return;
        }

        // Resolution
        var resolution = GetPlainString(period, "resolution");
        SetIfPresent(flat, "resolution", resolution);

        // Time interval
        if (period.TryGetProperty("timeInterval", out var interval))
        {
            var start = UnwrapString(interval, "start");
            var end = UnwrapString(interval, "end");

            SetIfPresent(flat, "periodStart", start);
            SetIfPresent(flat, "periodEnd", end);

            // D08 handler reads startDate/endDate
            SetIfPresent(flat, "startDate", start);
            SetIfPresent(flat, "endDate", end);
        }

        // Points extraction
        if (!period.TryGetProperty("Point", out var pointsEl) || pointsEl.ValueKind != JsonValueKind.Array)
            return;

        var points = new List<JsonElement>();
        foreach (var pt in pointsEl.EnumerateArray())
            points.Add(pt);

        if (points.Count == 0) return;

        // Determine output format based on business process and document type
        var isPriceData = IsPriceData(flat, businessProcess, documentType);

        if (isPriceData)
            ConvertToPricePoints(points, flat, resolution);
        else
            ConvertToObservations(points, flat, resolution);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Point → Observations (BRS-021, BRS-023: timestamp + kwh + quality)
    // ────────────────────────────────────────────────────────────────────────

    private static void ConvertToObservations(List<JsonElement> points, Dictionary<string, object?> flat, string? resolution)
    {
        var periodStart = flat.TryGetValue("periodStart", out var ps) && ps is string psStr
            ? DateTimeOffset.Parse(psStr)
            : (DateTimeOffset?)null;

        if (periodStart == null) return;

        var step = ParseResolution(resolution);
        var observations = new List<Dictionary<string, object?>>();

        foreach (var pt in points)
        {
            var position = UnwrapInt(pt, "position");
            if (position == null) continue;

            var timestamp = periodStart.Value + (position.Value - 1) * step;
            var kwh = GetDecimal(pt, "quantity");
            var quality = UnwrapString(pt, "quality");

            var obs = new Dictionary<string, object?>
            {
                ["timestamp"] = timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["kwh"] = kwh
            };
            if (quality != null)
                obs["quality"] = quality;

            observations.Add(obs);
        }

        flat["observations"] = observations;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Point → Price Points (BRS-031 D08: timestamp + price)
    // ────────────────────────────────────────────────────────────────────────

    private static void ConvertToPricePoints(List<JsonElement> points, Dictionary<string, object?> flat, string? resolution)
    {
        var periodStart = flat.TryGetValue("periodStart", out var ps) && ps is string psStr
            ? DateTimeOffset.Parse(psStr)
            : flat.TryGetValue("startDate", out var sd) && sd is string sdStr
                ? DateTimeOffset.Parse(sdStr)
                : (DateTimeOffset?)null;

        if (periodStart == null) return;

        var step = ParseResolution(resolution);
        var pricePoints = new List<Dictionary<string, object?>>();

        foreach (var pt in points)
        {
            var position = UnwrapInt(pt, "position");
            if (position == null) continue;

            var timestamp = periodStart.Value + (position.Value - 1) * step;
            var price = GetDecimal(pt, "quantity");

            pricePoints.Add(new Dictionary<string, object?>
            {
                ["timestamp"] = timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["price"] = price
            });
        }

        flat["points"] = pricePoints;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Determine if the data is price data (D08) based on businessReason or business process context.
    /// </summary>
    private static bool IsPriceData(Dictionary<string, object?> flat, string? businessProcess, string? documentType)
    {
        if (flat.TryGetValue("businessReason", out var br) && br is string reason && reason == "D08")
            return true;
        if (documentType is "D08")
            return true;
        if (businessProcess is "BRS-031" or "BRS-037")
        {
            // If we have chargeId but no gsrn on the series, it's price data
            if (flat.ContainsKey("chargeId"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Parse ISO 8601 duration to TimeSpan.
    /// </summary>
    private static TimeSpan ParseResolution(string? resolution) => resolution switch
    {
        "PT1H" => TimeSpan.FromHours(1),
        "PT15M" => TimeSpan.FromMinutes(15),
        "P1D" => TimeSpan.FromDays(1),
        "P1M" => TimeSpan.FromDays(30), // Approximation
        _ => TimeSpan.FromHours(1) // Default fallback
    };

    /// <summary>
    /// Unwrap CIM value wrapper: {"codingScheme":"A10","value":"X"} → "X",
    /// {"value":"Y"} → "Y", "plain" → "plain", 123 → "123".
    /// </summary>
    private static string? UnwrapString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return null;
        return UnwrapValue(el);
    }

    private static string? UnwrapValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object when el.TryGetProperty("value", out var v) => UnwrapValue(v),
            _ => null
        };
    }

    private static int? UnwrapInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetInt32(),
            JsonValueKind.Object when el.TryGetProperty("value", out var v) => v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null,
            _ => null
        };
    }

    private static string? GetPlainString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            _ => null
        };
    }

    private static decimal GetDecimal(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return 0m;
        return el.ValueKind == JsonValueKind.Number ? el.GetDecimal() : 0m;
    }

    private static void SetIfPresent(Dictionary<string, object?> dict, string key, string? value)
    {
        if (value != null)
            dict[key] = value;
    }
}
