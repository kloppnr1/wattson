using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles outbound BRS-027 — Request wholesale settlement data from DataHub.
/// Creates an RSM-017 CIM request payload and queues it in outbox.
///
/// Request: RSM-017/D21 → DataHub (DGL role)
/// Response: RSM-019 (wholesale settlement) → inbox → routed to Brs027Handler
///
/// ProcessType: D05 = Engrosfiksering
///
/// Grid area uses NDK coding scheme (NOT A10/GLN).
/// GLN fields use A10 coding scheme.
/// ChargeType filter is optional — when omitted, all charges are returned.
/// </summary>
public static class Brs027RequestHandler
{
    /// <summary>NDK = Danish grid area coding scheme.</summary>
    private const string CodingSchemeGridArea = "NDK";

    /// <summary>A10 = GS1 (GLN) coding scheme.</summary>
    private const string CodingSchemeGln = "A10";

    /// <summary>Charge type filter: mRID + type (D01=subscription, D02=fee, D03=tariff).</summary>
    public record ChargeTypeFilter(string ChargeId, string Type);

    public record WholesaleSettlementRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request wholesale settlement data from DataHub for a grid area and period.
    /// </summary>
    /// <param name="supplierGln">Our GLN (sender)</param>
    /// <param name="gridArea">Grid area code (e.g. "543")</param>
    /// <param name="startDate">Period start</param>
    /// <param name="endDate">Period end</param>
    /// <param name="energySupplierGln">Energy supplier GLN (usually same as sender). If null, uses supplierGln.</param>
    /// <param name="chargeTypeOwnerGln">Optional charge type owner filter</param>
    /// <param name="chargeTypes">Optional charge type filters</param>
    public static WholesaleSettlementRequestResult RequestWholesaleSettlement(
        GlnNumber supplierGln,
        string gridArea,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string? energySupplierGln = null,
        string? chargeTypeOwnerGln = null,
        IReadOnlyList<ChargeTypeFilter>? chargeTypes = null)
    {
        if (string.IsNullOrWhiteSpace(gridArea))
            throw new ArgumentException("Grid area is required.", nameof(gridArea));
        if (endDate <= startDate)
            throw new ArgumentException("End date must be after start date.", nameof(endDate));

        var process = BrsProcess.Create(
            ProcessType.EngrosAfregningAnmodning,
            ProcessRole.Initiator,
            "Created",
            effectiveDate: startDate);

        var transactionId = $"BRS027-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Anmodning om engrosafregning sendt til DataHub");
        process.MarkSubmitted(transactionId);

        // Energy supplier defaults to sender if not specified
        var esGln = energySupplierGln ?? supplierGln.Value;

        var seriesFields = new Dictionary<string, object?>
        {
            ["meteringGridArea_Domain.mRID"] = new { codingScheme = CodingSchemeGridArea, value = gridArea },
            ["energySupplier_MarketParticipant.mRID"] = new { codingScheme = CodingSchemeGln, value = esGln },
            ["start_DateAndOrTime.dateTime"] = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["end_DateAndOrTime.dateTime"] = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        if (chargeTypeOwnerGln is not null)
        {
            seriesFields["chargeTypeOwner_MarketParticipant.mRID"] = new { codingScheme = CodingSchemeGln, value = chargeTypeOwnerGln };
        }

        if (chargeTypes is { Count: > 0 })
        {
            seriesFields["ChargeType"] = chargeTypes.Select(ct => new Dictionary<string, object>
            {
                ["mRID"] = ct.ChargeId,
                ["type"] = new { value = ct.Type }
            }).ToList();
        }

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm017, "D05", supplierGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-017",
            senderGln: supplierGln.Value,
            receiverGln: CimDocumentBuilder.DataHubGln,
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-027");

        return new WholesaleSettlementRequestResult(process, outbox);
    }

    /// <summary>
    /// DataHub rejected our wholesale settlement request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Wholesale settlement data received — mark process as completed.
    /// The actual data is processed by Brs027Handler.
    /// </summary>
    public static void HandleDataReceived(BrsProcess process)
    {
        process.TransitionTo("DataReceived", "Engrosafregningsdata modtaget fra DataHub");
        process.MarkCompleted();
    }
}
