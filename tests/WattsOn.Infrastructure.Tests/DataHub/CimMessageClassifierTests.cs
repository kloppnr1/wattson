using WattsOn.Infrastructure.DataHub;

namespace WattsOn.Infrastructure.Tests.DataHub;

/// <summary>
/// Tests for CimMessageClassifier — CIM envelope → BRS/RSM classification.
/// No Docker or database required.
/// </summary>
public class CimMessageClassifierTests
{
    // ── Helper to build CIM JSON envelopes ──────────────────────────────
    // CIM JSON uses flat dotted property names (e.g., "process.processType" is a single key,
    // "sender_MarketParticipant.mRID" is a single key with a value-wrapper object).

    private static string BuildCimJson(
        string documentName,
        string? typeCode = null,
        string? processType = null,
        string? senderGln = "5790001330552",
        string? receiverGln = "5790000432752",
        string? mrid = "test-123")
    {
        var typeField = typeCode != null
            ? $"\"type\": {{\"value\": \"{typeCode}\"}},"
            : "";
        var processField = processType != null
            ? $"\"process.processType\": {{\"value\": \"{processType}\"}},"
            : "";
        var senderField = senderGln != null
            ? $"\"sender_MarketParticipant.mRID\": {{\"codingScheme\": \"A10\", \"value\": \"{senderGln}\"}},"
            : "";
        var receiverField = receiverGln != null
            ? $"\"receiver_MarketParticipant.mRID\": {{\"codingScheme\": \"A10\", \"value\": \"{receiverGln}\"}},"
            : "";

        return $$"""
        {
            "{{documentName}}": {
                "mRID": "{{mrid}}",
                {{typeField}}
                {{processField}}
                {{senderField}}
                {{receiverField}}
                "createdDateTime": "2025-01-01T00:00:00Z"
            }
        }
        """;
    }

    // ── BRS-001: Change of Supplier ─────────────────────────────────────

    [Fact]
    public void Classify_ChangeOfSupplier_Confirm_Returns_BRS001_RSM001()
    {
        var json = BuildCimJson("ConfirmRequestChangeOfSupplier_MarketDocument", processType: "E03");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-001", result.BusinessProcess);
        Assert.Equal("RSM-001", result.DocumentType);
        Assert.Equal("ConfirmRequestChangeOfSupplier_MarketDocument", result.DocumentName);
    }

    [Fact]
    public void Classify_ChangeOfSupplier_Reject_Returns_BRS001_RSM001()
    {
        var json = BuildCimJson("RejectRequestChangeOfSupplier_MarketDocument", typeCode: "A02");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-001", result.BusinessProcess);
        Assert.Equal("RSM-001", result.DocumentType);
    }

    // ── BRS-001 / RSM-004: End of Supply Notification ───────────────────

    [Fact]
    public void Classify_EndOfSupply_Notify_Returns_BRS001_RSM004()
    {
        var json = BuildCimJson("NotifyEndOfSupply_MarketDocument");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-001", result.BusinessProcess);
        Assert.Equal("RSM-004", result.DocumentType);
    }

    // ── BRS-002 / RSM-005: End of Supply Confirm/Reject ─────────────────

    [Fact]
    public void Classify_EndOfSupply_Confirm_Returns_BRS002_RSM005()
    {
        var json = BuildCimJson("ConfirmRequestEndOfSupply_MarketDocument");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-002", result.BusinessProcess);
        Assert.Equal("RSM-005", result.DocumentType);
    }

    [Fact]
    public void Classify_EndOfSupply_Reject_Returns_BRS002_RSM005()
    {
        var json = BuildCimJson("RejectRequestEndOfSupply_MarketDocument", typeCode: "A02");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-002", result.BusinessProcess);
        Assert.Equal("RSM-005", result.DocumentType);
    }

    // ── BRS-021 / RSM-012: Validated Measure Data ───────────────────────

    [Fact]
    public void Classify_ValidatedMeasureData_Notify_Returns_BRS021_RSM012()
    {
        var json = BuildCimJson("NotifyValidatedMeasureData_MarketDocument", processType: "E23");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-021", result.BusinessProcess);
        Assert.Equal("RSM-012", result.DocumentType);
    }

    // ── BRS-023 / RSM-014: Aggregated Measure Data ──────────────────────

    [Fact]
    public void Classify_AggregatedMeasureData_Returns_BRS023_RSM014()
    {
        var json = BuildCimJson("NotifyAggregatedMeasureData_MarketDocument", processType: "D04");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-023", result.BusinessProcess);
        Assert.Equal("RSM-014", result.DocumentType);
    }

    // ── BRS-027 / RSM-019: Wholesale ────────────────────────────────────

    [Fact]
    public void Classify_WholesaleSettlement_Returns_BRS027_RSM019()
    {
        var json = BuildCimJson("NotifyWholesaleSettlement_MarketDocument", processType: "D05");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-027", result.BusinessProcess);
        Assert.Equal("RSM-019", result.DocumentType);
    }

    [Fact]
    public void Classify_WholesaleServices_Returns_BRS027_RSM019()
    {
        var json = BuildCimJson("NotifyWholesaleServices_MarketDocument");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-027", result.BusinessProcess);
        Assert.Equal("RSM-019", result.DocumentType);
    }

    // ── BRS-031 / RSM-033: Price Info ───────────────────────────────────

    [Fact]
    public void Classify_PriceList_D18_Returns_BRS031_RSM033()
    {
        var json = BuildCimJson("NotifyPriceList_MarketDocument", processType: "D18");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-031", result.BusinessProcess);
        Assert.Equal("RSM-033", result.DocumentType);
    }

    [Fact]
    public void Classify_ChargeInformation_Returns_BRS031_RSM033()
    {
        var json = BuildCimJson("NotifyChargeInformation_MarketDocument", processType: "D18");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-031", result.BusinessProcess);
        Assert.Equal("RSM-033", result.DocumentType);
    }

    // ── BRS-037 / RSM-033: Charge Links ─────────────────────────────────

    [Fact]
    public void Classify_ChargeLinks_D17_Returns_BRS037_RSM033()
    {
        var json = BuildCimJson("NotifyChargeLinks_MarketDocument", processType: "D17");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-037", result.BusinessProcess);
        Assert.Equal("RSM-033", result.DocumentType);
    }

    // ── BRS-004 / RSM-020: New Metering Point ───────────────────────────

    [Fact]
    public void Classify_MeteringPoint_E04_Returns_BRS004_RSM020()
    {
        var json = BuildCimJson("ConfirmRequestMeteringPoint_MarketDocument", typeCode: "E04");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-004", result.BusinessProcess);
        Assert.Equal("RSM-020", result.DocumentType);
    }

    // ── BRS-006 / RSM-020: MP Characteristics Update ────────────────────

    [Fact]
    public void Classify_MeteringPoint_OtherType_Returns_BRS006_RSM020()
    {
        var json = BuildCimJson("NotifyMeteringPointCharacteristics_MarketDocument", typeCode: "E06");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-006", result.BusinessProcess);
        Assert.Equal("RSM-020", result.DocumentType);
    }

    // ── Fallback by processType ─────────────────────────────────────────

    [Theory]
    [InlineData("E03", "BRS-001")]
    [InlineData("E20", "BRS-002")]
    [InlineData("E65", "BRS-009")]
    [InlineData("E23", "BRS-021")]
    [InlineData("D04", "BRS-023")]
    [InlineData("D05", "BRS-027")]
    [InlineData("D18", "BRS-031")]
    [InlineData("D08", "BRS-031")]
    [InlineData("D17", "BRS-037")]
    [InlineData("D34", "BRS-003")]
    [InlineData("D35", "BRS-003")]
    [InlineData("D07", "BRS-003")]
    [InlineData("E04", "BRS-004")]
    [InlineData("E06", "BRS-006")]
    [InlineData("E07", "BRS-007")]
    [InlineData("E08", "BRS-008")]
    [InlineData("E09", "BRS-013")]
    [InlineData("E36", "BRS-036")]
    [InlineData("E44", "BRS-044")]
    public void Classify_Fallback_ProcessType_Returns_CorrectBrs(string processType, string expectedBrs)
    {
        // Use a document name that doesn't match any pattern
        var json = BuildCimJson("SomeUnknown_MarketDocument", processType: processType);
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal(expectedBrs, result.BusinessProcess);
        Assert.NotNull(result.DocumentType); // Should infer RSM from BRS
    }

    // ── Sender / Receiver GLN extraction ────────────────────────────────

    [Fact]
    public void Classify_ExtractsSenderGln()
    {
        var json = BuildCimJson("ConfirmRequestChangeOfSupplier_MarketDocument",
            senderGln: "5790001330552");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("5790001330552", result.SenderGln);
    }

    [Fact]
    public void Classify_ExtractsReceiverGln()
    {
        var json = BuildCimJson("ConfirmRequestChangeOfSupplier_MarketDocument",
            receiverGln: "5790000432752");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("5790000432752", result.ReceiverGln);
    }

    [Fact]
    public void Classify_PlainStringGln_ExtractsCorrectly()
    {
        // GLN as plain string (no value wrapper) — flat CIM property names
        var json = """
        {
            "ConfirmRequestChangeOfSupplier_MarketDocument": {
                "mRID": "test-456",
                "sender_MarketParticipant.mRID": "5790001330552",
                "receiver_MarketParticipant.mRID": "5790000432752"
            }
        }
        """;
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("5790001330552", result.SenderGln);
        Assert.Equal("5790000432752", result.ReceiverGln);
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void Classify_NullInput_ReturnsEmptyClassification()
    {
        var result = CimMessageClassifier.Classify(null);

        Assert.Null(result.BusinessProcess);
        Assert.Null(result.DocumentType);
        Assert.Null(result.SenderGln);
        Assert.Null(result.ReceiverGln);
        Assert.Null(result.DocumentName);
    }

    [Fact]
    public void Classify_EmptyString_ReturnsEmptyClassification()
    {
        var result = CimMessageClassifier.Classify("");

        Assert.Null(result.BusinessProcess);
        Assert.Null(result.DocumentType);
    }

    [Fact]
    public void Classify_WhitespaceString_ReturnsEmptyClassification()
    {
        var result = CimMessageClassifier.Classify("   ");

        Assert.Null(result.BusinessProcess);
    }

    [Fact]
    public void Classify_MalformedJson_ReturnsEmptyClassification()
    {
        var result = CimMessageClassifier.Classify("{ this is not valid JSON }}}");

        Assert.Null(result.BusinessProcess);
        Assert.Null(result.DocumentType);
        Assert.Null(result.SenderGln);
    }

    [Fact]
    public void Classify_ValidJsonNoMarketDocument_ReturnsEmptyClassification()
    {
        var result = CimMessageClassifier.Classify("""{"hello": "world"}""");

        Assert.Null(result.BusinessProcess);
        Assert.Null(result.DocumentName);
    }

    [Fact]
    public void Classify_UnknownDocumentName_NoProcessType_ReturnsNulls()
    {
        var json = BuildCimJson("SomethingRandom_MarketDocument");
        var result = CimMessageClassifier.Classify(json);

        Assert.Null(result.BusinessProcess);
        Assert.Null(result.DocumentType);
        Assert.Equal("SomethingRandom_MarketDocument", result.DocumentName);
        // But sender/receiver should still be extracted
        Assert.Equal("5790001330552", result.SenderGln);
        Assert.Equal("5790000432752", result.ReceiverGln);
    }

    [Fact]
    public void Classify_MeteringPoint_ProcessTypeE04_NullTypeCode_Returns_BRS004()
    {
        // processType E04 should also trigger BRS-004 via MeteringPoint document name
        var json = BuildCimJson("ConfirmRequestMeteringPoint_MarketDocument", processType: "E04");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("BRS-004", result.BusinessProcess);
        Assert.Equal("RSM-020", result.DocumentType);
    }

    // ── DocumentName is preserved ───────────────────────────────────────

    [Fact]
    public void Classify_PreservesDocumentName()
    {
        var json = BuildCimJson("NotifyAggregatedMeasureData_MarketDocument");
        var result = CimMessageClassifier.Classify(json);

        Assert.Equal("NotifyAggregatedMeasureData_MarketDocument", result.DocumentName);
    }
}
