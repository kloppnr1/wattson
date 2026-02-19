using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-015 — Opdatering af kundestamdata (Update of Customer Master Data).
/// Initiator process: supplier sends updated customer data to DataHub.
/// </summary>
public static class Brs015Handler
{
    public record CustomerUpdateData(
        string CustomerName,
        string? Cpr,
        string? Cvr,
        string? Email,
        string? Phone,
        Address? Address);

    public record CustomerUpdateResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Send customer data update to DataHub for a metering point.
    /// </summary>
    public static CustomerUpdateResult SendCustomerUpdate(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        GlnNumber supplierGln,
        CustomerUpdateData data)
    {
        if (string.IsNullOrWhiteSpace(data.CustomerName))
            throw new InvalidOperationException("Customer name is required for BRS-015.");

        if (data.CustomerName == "(ukendt)")
            throw new InvalidOperationException("Cannot update customer name to '(ukendt)' — use BRS-009 instead.");

        var process = BrsProcess.Create(
            ProcessType.CustomerStamdataOpdatering,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            effectiveDate);

        var transactionId = $"BRS015-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Kundestamdata opdatering sendt til DataHub");
        process.MarkSubmitted(transactionId);

        // Build CIM Series fields
        var seriesFields = new Dictionary<string, object?>
        {
            ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
            ["start_DateAndOrTime.dateTime"] = effectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["customerName"] = data.CustomerName,
        };
        if (data.Cpr is not null) seriesFields["customer.mRID"] = new { codingScheme = "CPR", value = data.Cpr };
        if (data.Cvr is not null) seriesFields["customer.mRID"] = new { codingScheme = "CVR", value = data.Cvr };
        if (data.Email is not null) seriesFields["electronicAddress.emailAddress"] = data.Email;
        if (data.Phone is not null) seriesFields["electronicAddress.phoneNumber"] = data.Phone;
        if (data.Address is not null)
        {
            seriesFields["mainAddress.streetDetail.name"] = data.Address.StreetName;
            seriesFields["mainAddress.streetDetail.number"] = data.Address.BuildingNumber;
            seriesFields["mainAddress.townDetail.code"] = data.Address.PostCode;
            seriesFields["mainAddress.townDetail.name"] = data.Address.CityName;
            if (data.Address.Floor is not null) seriesFields["mainAddress.streetDetail.floorIdentification"] = data.Address.Floor;
            if (data.Address.Suite is not null) seriesFields["mainAddress.streetDetail.suiteNumber"] = data.Address.Suite;
        }

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm027, "E34", supplierGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-027",
            senderGln: supplierGln.Value,
            receiverGln: "5790001330552",
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-015");

        return new CustomerUpdateResult(process, outbox);
    }

    /// <summary>
    /// DataHub confirmed the customer data update.
    /// </summary>
    public static void HandleConfirmation(BrsProcess process)
    {
        process.TransitionTo("Confirmed", "DataHub godkendt kundestamdata opdatering");
        process.MarkConfirmed();
        process.TransitionTo("Completed", "Kundestamdata opdatering gennemført");
        process.MarkCompleted();
    }

    /// <summary>
    /// DataHub rejected the customer data update.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }
}
