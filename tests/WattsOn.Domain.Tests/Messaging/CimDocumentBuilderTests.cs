using System.Text.Json;
using WattsOn.Domain.Messaging;

namespace WattsOn.Domain.Tests.Messaging;

public class CimDocumentBuilderTests
{
    private const string TestSenderGln = "5790000000005";
    private const string DataHubGln = "5790001330552";

    // ==================== Envelope Structure ====================

    [Theory]
    [InlineData(RsmDocumentType.Rsm001, "RequestChangeOfSupplier_MarketDocument")]
    [InlineData(RsmDocumentType.Rsm005, "RequestEndOfSupply_MarketDocument")]
    [InlineData(RsmDocumentType.Rsm027, "RequestChangeCustomerCharacteristics_MarketDocument")]
    [InlineData(RsmDocumentType.Rsm032, "RequestChargeLinks_MarketDocument")]
    [InlineData(RsmDocumentType.Rsm035, "RequestPrices_MarketDocument")]
    public void Build_HasCorrectRootElement(RsmDocumentType rsmType, string expectedRoot)
    {
        var json = CimDocumentBuilder
            .Create(rsmType, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(doc.TryGetProperty(expectedRoot, out _),
            $"Expected root element '{expectedRoot}' not found in: {json}");
    }

    [Fact]
    public void Build_MRIDIsValidGuid()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var mrid = doc.GetProperty("RequestChangeOfSupplier_MarketDocument")
            .GetProperty("mRID").GetString();

        Assert.True(Guid.TryParse(mrid, out _), $"mRID is not a valid GUID: {mrid}");
    }

    [Fact]
    public void Build_SenderGlnAndRole()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        var senderMrid = doc.GetProperty("sender_MarketParticipant.mRID");
        Assert.Equal("A10", senderMrid.GetProperty("codingScheme").GetString());
        Assert.Equal(TestSenderGln, senderMrid.GetProperty("value").GetString());

        var senderRole = doc.GetProperty("sender_MarketParticipant.marketRole.type");
        Assert.Equal("DDQ", senderRole.GetProperty("value").GetString());
    }

    [Fact]
    public void Build_ReceiverGlnAndRole()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        var receiverMrid = doc.GetProperty("receiver_MarketParticipant.mRID");
        Assert.Equal("A10", receiverMrid.GetProperty("codingScheme").GetString());
        Assert.Equal(DataHubGln, receiverMrid.GetProperty("value").GetString());

        var receiverRole = doc.GetProperty("receiver_MarketParticipant.marketRole.type");
        Assert.Equal("DGL", receiverRole.GetProperty("value").GetString());
    }

    [Fact]
    public void Build_BusinessSectorIsElectricity()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        Assert.Equal("23", doc.GetProperty("businessSector.type").GetProperty("value").GetString());
    }

    [Fact]
    public void Build_CreatedDateTimeIsUtc()
    {
        var beforeUtc = DateTimeOffset.UtcNow;

        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        var createdStr = doc.GetProperty("createdDateTime").GetString()!;
        Assert.EndsWith("Z", createdStr);

        var created = DateTimeOffset.Parse(createdStr);
        Assert.True(created >= beforeUtc.AddSeconds(-1));
        Assert.True(created <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Build_ProcessTypeIsSet()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        Assert.Equal("E03", doc.GetProperty("process.processType").GetProperty("value").GetString());
    }

    // ==================== Document Type Codes ====================

    [Theory]
    [InlineData(RsmDocumentType.Rsm001, "392")]
    [InlineData(RsmDocumentType.Rsm005, "392")]
    [InlineData(RsmDocumentType.Rsm027, "D15")]
    [InlineData(RsmDocumentType.Rsm032, "E0G")]
    [InlineData(RsmDocumentType.Rsm035, "E0G")]
    public void Build_TypeCodeIsCorrect(RsmDocumentType rsmType, string expectedTypeCode)
    {
        var json = CimDocumentBuilder
            .Create(rsmType, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var rootName = RsmDocumentConfig.Get(rsmType).MarketDocumentName;
        var typeValue = doc.GetProperty(rootName)
            .GetProperty("type").GetProperty("value").GetString();

        Assert.Equal(expectedTypeCode, typeValue);
    }

    // ==================== Series ====================

    [Fact]
    public void Build_WithSeries_SeriesHasMRID()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = "571313180000000005" }
            })
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        var series = doc.GetProperty("Series");
        Assert.Equal(JsonValueKind.Array, series.ValueKind);
        Assert.Equal(1, series.GetArrayLength());

        var firstSeries = series[0];
        var seriesMrid = firstSeries.GetProperty("mRID").GetString();
        Assert.True(Guid.TryParse(seriesMrid, out _), $"Series mRID is not a valid GUID: {seriesMrid}");
    }

    [Fact]
    public void Build_WithoutSeries_NoSeriesProperty()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        Assert.False(doc.TryGetProperty("Series", out _));
    }

    [Fact]
    public void Build_MultipleSeries_AllIncluded()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .AddSeries(new Dictionary<string, object?> { ["field1"] = "a" })
            .AddSeries(new Dictionary<string, object?> { ["field2"] = "b" })
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        var series = doc.GetProperty("Series");
        Assert.Equal(2, series.GetArrayLength());
    }

    // ==================== RSM-001 (BRS-001/003) — Supplier Change ====================

    [Fact]
    public void Rsm001_SeriesContainsGsrnAndDate()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = "571313180000000005" },
                ["start_DateAndOrTime.dateTime"] = "2025-01-15T00:00:00Z",
            })
            .Build();

        var series = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument")
            .GetProperty("Series")[0];

        Assert.Equal("571313180000000005",
            series.GetProperty("marketEvaluationPoint.mRID").GetProperty("value").GetString());
        Assert.Equal("2025-01-15T00:00:00Z",
            series.GetProperty("start_DateAndOrTime.dateTime").GetString());
    }

    // ==================== RSM-005 (BRS-002/010/011) — End of Supply ====================

    [Fact]
    public void Rsm005_EndOfSupplyEnvelope()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm005, "E20", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = "571313180000000005" },
                ["end_DateAndOrTime.dateTime"] = "2025-03-01T00:00:00Z",
            })
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(doc.TryGetProperty("RequestEndOfSupply_MarketDocument", out var marketDoc));
        Assert.Equal("E20", marketDoc.GetProperty("process.processType").GetProperty("value").GetString());
        Assert.Equal("392", marketDoc.GetProperty("type").GetProperty("value").GetString());

        var series = marketDoc.GetProperty("Series")[0];
        Assert.Equal("571313180000000005",
            series.GetProperty("marketEvaluationPoint.mRID").GetProperty("value").GetString());
    }

    // ==================== RSM-027 (BRS-015) — Customer Data ====================

    [Fact]
    public void Rsm027_CustomerDataEnvelope()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm027, "E34", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = "571313180000000005" },
                ["customerName"] = "Anders Andersen",
                ["customer.mRID"] = new { codingScheme = "CPR", value = "0101901234" },
                ["electronicAddress.emailAddress"] = "anders@test.dk",
            })
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(doc.TryGetProperty("RequestChangeCustomerCharacteristics_MarketDocument", out var marketDoc));
        Assert.Equal("D15", marketDoc.GetProperty("type").GetProperty("value").GetString());

        var series = marketDoc.GetProperty("Series")[0];
        Assert.Equal("Anders Andersen", series.GetProperty("customerName").GetString());
        Assert.Equal("CPR", series.GetProperty("customer.mRID").GetProperty("codingScheme").GetString());
        Assert.Equal("anders@test.dk", series.GetProperty("electronicAddress.emailAddress").GetString());
    }

    // ==================== RSM-032 (BRS-038) — Charge Links ====================

    [Fact]
    public void Rsm032_ChargeLinksEnvelope()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm032, "E0G", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = "571313180000000005" },
                ["start_DateAndOrTime.dateTime"] = "2025-01-01T00:00:00Z",
                ["end_DateAndOrTime.dateTime"] = "2025-12-31T00:00:00Z",
            })
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(doc.TryGetProperty("RequestChargeLinks_MarketDocument", out var marketDoc));
        Assert.Equal("E0G", marketDoc.GetProperty("type").GetProperty("value").GetString());

        var series = marketDoc.GetProperty("Series")[0];
        Assert.Equal("571313180000000005",
            series.GetProperty("marketEvaluationPoint.mRID").GetProperty("value").GetString());
        Assert.Equal("2025-01-01T00:00:00Z",
            series.GetProperty("start_DateAndOrTime.dateTime").GetString());
        Assert.Equal("2025-12-31T00:00:00Z",
            series.GetProperty("end_DateAndOrTime.dateTime").GetString());
    }

    // ==================== RSM-035 (BRS-034) — Prices ====================

    [Fact]
    public void Rsm035_PriceInfoEnvelope()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm035, "E0G", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["chargeTypeOwner_MarketParticipant.mRID"] = new { codingScheme = "A10", value = "5790000000013" },
                ["chargeType"] = "Tarif",
                ["chargeType.mRID"] = "CHARGE-001",
                ["start_DateAndOrTime.dateTime"] = "2025-01-01T00:00:00Z",
            })
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(doc.TryGetProperty("RequestPrices_MarketDocument", out var marketDoc));
        Assert.Equal("E0G", marketDoc.GetProperty("type").GetProperty("value").GetString());

        var series = marketDoc.GetProperty("Series")[0];
        Assert.Equal("5790000000013",
            series.GetProperty("chargeTypeOwner_MarketParticipant.mRID").GetProperty("value").GetString());
        Assert.Equal("Tarif", series.GetProperty("chargeType").GetString());
        Assert.Equal("CHARGE-001", series.GetProperty("chargeType.mRID").GetString());
    }

    [Fact]
    public void Rsm035_PriceSeriesEnvelope()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm035, "D48", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["start_DateAndOrTime.dateTime"] = "2025-01-01T00:00:00Z",
                ["end_DateAndOrTime.dateTime"] = "2025-06-01T00:00:00Z",
            })
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestPrices_MarketDocument");

        Assert.Equal("D48", doc.GetProperty("process.processType").GetProperty("value").GetString());
    }

    // ==================== Custom Roles ====================

    [Fact]
    public void Build_CustomSenderAndReceiverRoles()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm001, "E03", TestSenderGln,
                senderRole: "DDM",
                receiverGln: "5790000000128",
                receiverRole: "DDQ")
            .Build();

        var doc = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestChangeOfSupplier_MarketDocument");

        Assert.Equal("DDM", doc.GetProperty("sender_MarketParticipant.marketRole.type").GetProperty("value").GetString());
        Assert.Equal("5790000000128", doc.GetProperty("receiver_MarketParticipant.mRID").GetProperty("value").GetString());
        Assert.Equal("DDQ", doc.GetProperty("receiver_MarketParticipant.marketRole.type").GetProperty("value").GetString());
    }

    // ==================== Null Handling ====================

    [Fact]
    public void Build_NullSeriesFieldsAreOmitted()
    {
        var json = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm005, "E20", TestSenderGln)
            .AddSeries(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = "571313180000000005" },
                ["end_DateAndOrTime.dateTime"] = null,
            })
            .Build();

        var series = JsonSerializer.Deserialize<JsonElement>(json)
            .GetProperty("RequestEndOfSupply_MarketDocument")
            .GetProperty("Series")[0];

        Assert.False(series.TryGetProperty("end_DateAndOrTime.dateTime", out _),
            "Null fields should be omitted from CIM JSON");
    }
}
