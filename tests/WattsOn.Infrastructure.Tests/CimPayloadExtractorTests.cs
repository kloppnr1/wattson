using System.Text.Json;
using WattsOn.Worker.Routing;

namespace WattsOn.Infrastructure.Tests;

public class CimPayloadExtractorTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static bool GetBool(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.GetBoolean();

    // ─────────────────────────────────────────────────────────────────────────
    // Non-CIM passthrough
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NonCim_FlatJson_PassedThrough()
    {
        var json = """{"gsrn":"571313180400000028","effectiveDate":"2026-03-01T00:00:00Z"}""";

        var result = CimPayloadExtractor.ExtractPayload(json);

        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
        Assert.Equal("2026-03-01T00:00:00Z", GetString(result, "effectiveDate"));
    }

    [Fact]
    public void NullPayload_ReturnsDefault()
    {
        var result = CimPayloadExtractor.ExtractPayload(null);
        Assert.Equal(JsonValueKind.Undefined, result.ValueKind);
    }

    [Fact]
    public void EmptyPayload_ReturnsDefault()
    {
        var result = CimPayloadExtractor.ExtractPayload("");
        Assert.Equal(JsonValueKind.Undefined, result.ValueKind);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-001 Supplier Change Confirm (MktActivityRecord)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs001_SupplierChangeConfirm_ExtractsGsrnTransactionIdEffectiveDate()
    {
        var json = """
        {
            "ConfirmRequestChangeOfSupplier_MarketDocument": {
                "mRID": "doc-uuid-002",
                "type": {"value": "A01"},
                "process.processType": {"value": "E03"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-001",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "start_DateAndOrTime.dateTime": "2026-03-01T00:00:00Z"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-001", "RSM-001");

        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
        Assert.Equal("txn-uuid-001", GetString(result, "transactionId"));
        Assert.Equal("2026-03-01T00:00:00Z", GetString(result, "effectiveDate"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-001 Lose Customer (RSM-004 with newSupplierGln)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs001_LoseCustomer_ExtractsNewSupplierGln()
    {
        var json = """
        {
            "NotifyEndOfSupply_MarketDocument": {
                "mRID": "doc-uuid-003",
                "type": {"value": "E44"},
                "process.processType": {"value": "E03"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-002",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "start_DateAndOrTime.dateTime": "2026-04-01T00:00:00Z",
                    "energySupplier_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000000005"}
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-001", "RSM-004");

        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
        Assert.Equal("2026-04-01T00:00:00Z", GetString(result, "effectiveDate"));
        Assert.Equal("5790000000005", GetString(result, "newSupplierGln"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-002 Rejection (type A02)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs002_Rejection_SetsRejectedFlag()
    {
        var json = """
        {
            "RejectRequestEndOfSupply_MarketDocument": {
                "mRID": "doc-uuid-004",
                "type": {"value": "A02"},
                "process.processType": {"value": "E02"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-003",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "reason.text": "Invalid metering point"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-002", "RSM-005");

        Assert.True(GetBool(result, "rejected"));
        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
        Assert.Equal("Invalid metering point", GetString(result, "reason"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-004 New Metering Point (all fields + address)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs004_NewMeteringPoint_ExtractsAllFieldsAndAddress()
    {
        var json = """
        {
            "NotifyNewMeteringPoint_MarketDocument": {
                "mRID": "doc-uuid-005",
                "type": {"value": "A01"},
                "process.processType": {"value": "E04"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610976"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-004",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000099"},
                    "marketEvaluationPoint.type": {"value": "E17"},
                    "marketEvaluationPoint.meteringMethod": {"value": "D01"},
                    "marketEvaluationPoint.settlementMethod": {"value": "E02"},
                    "marketEvaluationPoint.resolution": {"value": "PT1H"},
                    "marketEvaluationPoint.connectionState": {"value": "D03"},
                    "meteringGridArea_Domain.mRID": {"codingScheme": "NDK", "value": "DK1"},
                    "gridCompany_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610976"},
                    "linked_MarketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000088"},
                    "usagePointLocation.mainAddress.streetDetail.name": "Testvej",
                    "usagePointLocation.mainAddress.streetDetail.number": "42",
                    "usagePointLocation.mainAddress.townDetail.code": "2100",
                    "usagePointLocation.mainAddress.townDetail.name": "København Ø",
                    "usagePointLocation.mainAddress.streetDetail.floorIdentification": "3",
                    "usagePointLocation.mainAddress.streetDetail.suiteNumber": "tv"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-004", "RSM-020");

        Assert.Equal("571313180400000099", GetString(result, "gsrn"));
        Assert.Equal("E17", GetString(result, "type"));
        Assert.Equal("D01", GetString(result, "art"));
        Assert.Equal("E02", GetString(result, "settlementMethod"));
        Assert.Equal("PT1H", GetString(result, "resolution"));
        Assert.Equal("D03", GetString(result, "connectionState"));
        Assert.Equal("DK1", GetString(result, "gridArea"));
        Assert.Equal("5790000610976", GetString(result, "gridCompanyGln"));
        Assert.Equal("571313180400000088", GetString(result, "parentGsrn"));

        // Address
        Assert.True(result.TryGetProperty("address", out var addr));
        Assert.Equal("Testvej", addr.GetProperty("streetName").GetString());
        Assert.Equal("42", addr.GetProperty("buildingNumber").GetString());
        Assert.Equal("2100", addr.GetProperty("postCode").GetString());
        Assert.Equal("København Ø", addr.GetProperty("cityName").GetString());
        Assert.Equal("3", addr.GetProperty("floor").GetString());
        Assert.Equal("tv", addr.GetProperty("suite").GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-021 Time Series (Series with Period+Points → observations)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs021_TimeSeries_ConvertsPointsToObservationsWithTimestamps()
    {
        var json = """
        {
            "NotifyValidatedMeasureData_MarketDocument": {
                "mRID": "doc-uuid-001",
                "businessSector.type": {"value": "23"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "process.processType": {"value": "E23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "type": {"value": "E66"},
                "Series": [{
                    "mRID": "series-uuid-001",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "marketEvaluationPoint.type": {"value": "E17"},
                    "product": "8716867000030",
                    "quantity_Measure_Unit.name": {"value": "KWH"},
                    "Period": {
                        "resolution": "PT1H",
                        "timeInterval": {
                            "start": {"value": "2026-01-31T23:00:00Z"},
                            "end": {"value": "2026-02-28T23:00:00Z"}
                        },
                        "Point": [
                            {"position": {"value": 1}, "quality": {"value": "A03"}, "quantity": 0.543},
                            {"position": {"value": 2}, "quality": {"value": "A03"}, "quantity": 1.234},
                            {"position": {"value": 3}, "quality": {"value": "A03"}, "quantity": 0.891}
                        ]
                    }
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-021", "RSM-012");

        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
        Assert.Equal("series-uuid-001", GetString(result, "transactionId"));
        Assert.Equal("2026-01-31T23:00:00Z", GetString(result, "periodStart"));
        Assert.Equal("2026-02-28T23:00:00Z", GetString(result, "periodEnd"));
        Assert.Equal("PT1H", GetString(result, "resolution"));

        // Observations
        Assert.True(result.TryGetProperty("observations", out var obsArray));
        Assert.Equal(JsonValueKind.Array, obsArray.ValueKind);
        var obs = obsArray.EnumerateArray().ToList();
        Assert.Equal(3, obs.Count);

        // Position 1 → periodStart + 0*1h = 2026-01-31T23:00:00Z
        Assert.Equal("2026-01-31T23:00:00Z", obs[0].GetProperty("timestamp").GetString());
        Assert.Equal(0.543m, obs[0].GetProperty("kwh").GetDecimal());
        Assert.Equal("A03", obs[0].GetProperty("quality").GetString());

        // Position 2 → periodStart + 1*1h = 2026-02-01T00:00:00Z
        Assert.Equal("2026-02-01T00:00:00Z", obs[1].GetProperty("timestamp").GetString());
        Assert.Equal(1.234m, obs[1].GetProperty("kwh").GetDecimal());

        // Position 3 → periodStart + 2*1h = 2026-02-01T01:00:00Z
        Assert.Equal("2026-02-01T01:00:00Z", obs[2].GetProperty("timestamp").GetString());
        Assert.Equal(0.891m, obs[2].GetProperty("kwh").GetDecimal());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-023 Aggregated Data (observations from Points)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs023_AggregatedData_ExtractsGridAreaAndObservations()
    {
        var json = """
        {
            "NotifyAggregatedMeasureData_MarketDocument": {
                "mRID": "doc-uuid-010",
                "type": {"value": "E31"},
                "process.processType": {"value": "D03"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "Series": [{
                    "mRID": "agg-series-001",
                    "marketEvaluationPoint.type": {"value": "E17"},
                    "meteringGridArea_Domain.mRID": {"codingScheme": "NDK", "value": "DK1"},
                    "Period": {
                        "resolution": "PT1H",
                        "timeInterval": {
                            "start": {"value": "2026-01-15T00:00:00Z"},
                            "end": {"value": "2026-01-16T00:00:00Z"}
                        },
                        "Point": [
                            {"position": {"value": 1}, "quality": {"value": "A03"}, "quantity": 150.0},
                            {"position": {"value": 2}, "quality": {"value": "A03"}, "quantity": 140.0}
                        ]
                    }
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-023", "RSM-014");

        Assert.Equal("DK1", GetString(result, "gridArea"));
        Assert.Equal("E17", GetString(result, "meteringPointType"));
        Assert.Equal("2026-01-15T00:00:00Z", GetString(result, "periodStart"));
        Assert.Equal("2026-01-16T00:00:00Z", GetString(result, "periodEnd"));

        Assert.True(result.TryGetProperty("observations", out var obsArr));
        var obs = obsArr.EnumerateArray().ToList();
        Assert.Equal(2, obs.Count);
        Assert.Equal("2026-01-15T00:00:00Z", obs[0].GetProperty("timestamp").GetString());
        Assert.Equal(150.0m, obs[0].GetProperty("kwh").GetDecimal());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-031 D18 Charge Info
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs031_D18_ChargeInfo_ExtractsChargeFields()
    {
        var json = """
        {
            "NotifyPriceList_MarketDocument": {
                "mRID": "doc-uuid-006",
                "type": {"value": "D18"},
                "process.processType": {"value": "D18"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610099"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-d18",
                    "chargeType.mRID": "NET-TARIF-DK1",
                    "chargeType.type": {"value": "Tarif"},
                    "chargeType.name": "Nettarif DK1",
                    "chargeType.VATexempt": false,
                    "chargeType.owner_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610099"},
                    "start_DateAndOrTime.dateTime": "2026-01-01T00:00:00Z",
                    "end_DateAndOrTime.dateTime": "2027-01-01T00:00:00Z",
                    "marketEvaluationPoint.resolution": {"value": "PT1H"}
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-031", "RSM-033");

        Assert.Equal("D18", GetString(result, "businessReason"));
        Assert.Equal("NET-TARIF-DK1", GetString(result, "chargeId"));
        Assert.Equal("Tarif", GetString(result, "priceType"));
        Assert.Equal("Nettarif DK1", GetString(result, "description"));
        Assert.Equal("5790000610099", GetString(result, "ownerGln"));
        Assert.Equal("2026-01-01T00:00:00Z", GetString(result, "effectiveDate"));
        Assert.Equal("2027-01-01T00:00:00Z", GetString(result, "stopDate"));
        Assert.Equal("PT1H", GetString(result, "resolution"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-031 D08 Charge Prices (Series Points → price points)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs031_D08_ChargePrices_ConvertsPointsToPricePoints()
    {
        var json = """
        {
            "NotifyPriceList_MarketDocument": {
                "mRID": "doc-uuid-007",
                "type": {"value": "D08"},
                "process.processType": {"value": "D08"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610099"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "Series": [{
                    "mRID": "price-series-001",
                    "chargeType.mRID": "NET-TARIF-DK1",
                    "chargeType.owner_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610099"},
                    "Period": {
                        "resolution": "PT1H",
                        "timeInterval": {
                            "start": {"value": "2026-01-15T00:00:00Z"},
                            "end": {"value": "2026-01-16T00:00:00Z"}
                        },
                        "Point": [
                            {"position": {"value": 1}, "quality": {"value": "A03"}, "quantity": 0.2345},
                            {"position": {"value": 2}, "quality": {"value": "A03"}, "quantity": 0.3456},
                            {"position": {"value": 3}, "quality": {"value": "A03"}, "quantity": 1.5000}
                        ]
                    }
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-031", "D08");

        Assert.Equal("D08", GetString(result, "businessReason"));
        Assert.Equal("NET-TARIF-DK1", GetString(result, "chargeId"));
        Assert.Equal("5790000610099", GetString(result, "ownerGln"));
        Assert.Equal("2026-01-15T00:00:00Z", GetString(result, "startDate"));
        Assert.Equal("2026-01-16T00:00:00Z", GetString(result, "endDate"));

        Assert.True(result.TryGetProperty("points", out var ptsArr));
        var pts = ptsArr.EnumerateArray().ToList();
        Assert.Equal(3, pts.Count);

        Assert.Equal("2026-01-15T00:00:00Z", pts[0].GetProperty("timestamp").GetString());
        Assert.Equal(0.2345m, pts[0].GetProperty("price").GetDecimal());

        Assert.Equal("2026-01-15T01:00:00Z", pts[1].GetProperty("timestamp").GetString());
        Assert.Equal(0.3456m, pts[1].GetProperty("price").GetDecimal());

        Assert.Equal("2026-01-15T02:00:00Z", pts[2].GetProperty("timestamp").GetString());
        Assert.Equal(1.5000m, pts[2].GetProperty("price").GetDecimal());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-031 D17 Price Links
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs031_D17_PriceLinks_ExtractsGsrnChargeIdOwnerLinkDates()
    {
        var json = """
        {
            "NotifyChargeLinks_MarketDocument": {
                "mRID": "doc-uuid-008",
                "type": {"value": "D17"},
                "process.processType": {"value": "D17"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610099"},
                "sender_MarketParticipant.marketRole.type": {"value": "DDZ"},
                "receiver_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000432752"},
                "receiver_MarketParticipant.marketRole.type": {"value": "DDQ"},
                "createdDateTime": "2026-02-20T04:00:00Z",
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-d17",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "ChargeType.mRID": "NET-TARIF-DK1",
                    "ChargeType.owner_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610099"},
                    "start_DateAndOrTime.dateTime": "2026-01-01T00:00:00Z",
                    "end_DateAndOrTime.dateTime": "2027-01-01T00:00:00Z"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-031", "D17");

        Assert.Equal("D17", GetString(result, "businessReason"));
        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
        Assert.Equal("NET-TARIF-DK1", GetString(result, "chargeId"));
        Assert.Equal("5790000610099", GetString(result, "ownerGln"));
        Assert.Equal("2026-01-01T00:00:00Z", GetString(result, "linkStart"));
        Assert.Equal("2027-01-01T00:00:00Z", GetString(result, "linkEnd"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Value unwrapping variants
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValueUnwrap_CodingSchemeAndValue_ExtractsValue()
    {
        var json = """
        {
            "Test_MarketDocument": {
                "mRID": "doc-test",
                "type": {"value": "A01"},
                "process.processType": {"value": "E03"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "MktActivityRecord": [{
                    "mRID": "txn-test",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"}
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json);
        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
    }

    [Fact]
    public void ValueUnwrap_ValueOnly_ExtractsValue()
    {
        var json = """
        {
            "Test_MarketDocument": {
                "mRID": "doc-test",
                "type": {"value": "A01"},
                "process.processType": {"value": "E03"},
                "sender_MarketParticipant.mRID": {"value": "5790001330552"},
                "MktActivityRecord": [{
                    "mRID": "txn-test",
                    "marketEvaluationPoint.type": {"value": "E17"}
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json);
        Assert.Equal("E17", GetString(result, "type"));
    }

    [Fact]
    public void ValueUnwrap_PlainString_PassesThrough()
    {
        var json = """
        {
            "Test_MarketDocument": {
                "mRID": "doc-test",
                "type": {"value": "A01"},
                "process.processType": {"value": "E03"},
                "sender_MarketParticipant.mRID": {"value": "5790001330552"},
                "MktActivityRecord": [{
                    "mRID": "txn-plain-string",
                    "start_DateAndOrTime.dateTime": "2026-03-01T00:00:00Z"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json);
        Assert.Equal("txn-plain-string", GetString(result, "transactionId"));
        Assert.Equal("2026-03-01T00:00:00Z", GetString(result, "effectiveDate"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Period as object vs array
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Period_AsObject_ExtractedCorrectly()
    {
        var json = """
        {
            "NotifyValidatedMeasureData_MarketDocument": {
                "mRID": "doc-period-obj",
                "type": {"value": "E66"},
                "process.processType": {"value": "E23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "Series": [{
                    "mRID": "series-period-obj",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "Period": {
                        "resolution": "PT15M",
                        "timeInterval": {
                            "start": {"value": "2026-01-15T00:00:00Z"},
                            "end": {"value": "2026-01-16T00:00:00Z"}
                        },
                        "Point": [
                            {"position": {"value": 1}, "quality": {"value": "A03"}, "quantity": 0.1}
                        ]
                    }
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-021", "RSM-012");

        Assert.Equal("PT15M", GetString(result, "resolution"));
        Assert.Equal("2026-01-15T00:00:00Z", GetString(result, "periodStart"));

        Assert.True(result.TryGetProperty("observations", out var obs));
        var list = obs.EnumerateArray().ToList();
        Assert.Single(list);
        Assert.Equal("2026-01-15T00:00:00Z", list[0].GetProperty("timestamp").GetString());
    }

    [Fact]
    public void Period_AsArray_ExtractedCorrectly()
    {
        var json = """
        {
            "NotifyValidatedMeasureData_MarketDocument": {
                "mRID": "doc-period-arr",
                "type": {"value": "E66"},
                "process.processType": {"value": "E23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "Series": [{
                    "mRID": "series-period-arr",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "Period": [{
                        "resolution": "PT1H",
                        "timeInterval": {
                            "start": {"value": "2026-01-15T00:00:00Z"},
                            "end": {"value": "2026-01-16T00:00:00Z"}
                        },
                        "Point": [
                            {"position": {"value": 1}, "quality": {"value": "A03"}, "quantity": 0.5},
                            {"position": {"value": 2}, "quality": {"value": "A03"}, "quantity": 0.6}
                        ]
                    }]
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-021", "RSM-012");

        Assert.Equal("PT1H", GetString(result, "resolution"));
        Assert.True(result.TryGetProperty("observations", out var obs));
        var list = obs.EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("2026-01-15T00:00:00Z", list[0].GetProperty("timestamp").GetString());
        Assert.Equal("2026-01-15T01:00:00Z", list[1].GetProperty("timestamp").GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // D17 ownerGln fallback to sender GLN
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OwnerGln_FallsBackToSenderGln_WhenNotOnRecord()
    {
        var json = """
        {
            "NotifyChargeLinks_MarketDocument": {
                "mRID": "doc-uuid-fallback",
                "type": {"value": "D17"},
                "process.processType": {"value": "D17"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790000610099"},
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-fallback",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "start_DateAndOrTime.dateTime": "2026-01-01T00:00:00Z"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-031", "D17");

        Assert.Equal("5790000610099", GetString(result, "ownerGln"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal keys (_senderGln) are cleaned from output
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InternalKeys_AreRemovedFromOutput()
    {
        var json = """
        {
            "Test_MarketDocument": {
                "mRID": "doc-test-clean",
                "type": {"value": "A01"},
                "process.processType": {"value": "E03"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "MktActivityRecord": [{
                    "mRID": "txn-test-clean"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json);

        Assert.False(result.TryGetProperty("_senderGln", out _));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PT15M resolution computes correct timestamps
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolution_PT15M_ComputesCorrectTimestamps()
    {
        var json = """
        {
            "NotifyValidatedMeasureData_MarketDocument": {
                "mRID": "doc-15m",
                "type": {"value": "E66"},
                "process.processType": {"value": "E23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "Series": [{
                    "mRID": "series-15m",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "Period": {
                        "resolution": "PT15M",
                        "timeInterval": {
                            "start": {"value": "2026-01-15T00:00:00Z"},
                            "end": {"value": "2026-01-15T01:00:00Z"}
                        },
                        "Point": [
                            {"position": {"value": 1}, "quality": {"value": "A03"}, "quantity": 0.1},
                            {"position": {"value": 2}, "quality": {"value": "A03"}, "quantity": 0.2},
                            {"position": {"value": 3}, "quality": {"value": "A03"}, "quantity": 0.3},
                            {"position": {"value": 4}, "quality": {"value": "A03"}, "quantity": 0.4}
                        ]
                    }
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-021", "RSM-012");

        Assert.True(result.TryGetProperty("observations", out var obs));
        var list = obs.EnumerateArray().ToList();
        Assert.Equal(4, list.Count);
        Assert.Equal("2026-01-15T00:00:00Z", list[0].GetProperty("timestamp").GetString());
        Assert.Equal("2026-01-15T00:15:00Z", list[1].GetProperty("timestamp").GetString());
        Assert.Equal("2026-01-15T00:30:00Z", list[2].GetProperty("timestamp").GetString());
        Assert.Equal("2026-01-15T00:45:00Z", list[3].GetProperty("timestamp").GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-003 correction fields (businessReason D34/D35)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brs003_CorrectionAccepted_ExtractsBusinessReasonD34()
    {
        var json = """
        {
            "ConfirmCorrectionOfSupplierSwitch_MarketDocument": {
                "mRID": "doc-uuid-d34",
                "type": {"value": "A01"},
                "process.processType": {"value": "D34"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-d34",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "start_DateAndOrTime.dateTime": "2026-01-01T00:00:00Z"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-003", "RSM-004");

        Assert.Equal("D34", GetString(result, "businessReason"));
        Assert.Equal("571313180400000028", GetString(result, "gsrn"));
        Assert.Equal("2026-01-01T00:00:00Z", GetString(result, "effectiveDate"));
    }

    [Fact]
    public void Brs003_CorrectionRejected_ExtractsBusinessReasonD35()
    {
        var json = """
        {
            "RejectCorrectionOfSupplierSwitch_MarketDocument": {
                "mRID": "doc-uuid-d35",
                "type": {"value": "A02"},
                "process.processType": {"value": "D35"},
                "businessSector.type": {"value": "23"},
                "sender_MarketParticipant.mRID": {"codingScheme": "A10", "value": "5790001330552"},
                "MktActivityRecord": [{
                    "mRID": "txn-uuid-d35",
                    "marketEvaluationPoint.mRID": {"codingScheme": "A10", "value": "571313180400000028"},
                    "reason.text": "Not within allowed period"
                }]
            }
        }
        """;

        var result = CimPayloadExtractor.ExtractPayload(json, "BRS-003", "RSM-004");

        Assert.Equal("D35", GetString(result, "businessReason"));
        Assert.True(GetBool(result, "rejected"));
        Assert.Equal("Not within allowed period", GetString(result, "reason"));
    }
}
