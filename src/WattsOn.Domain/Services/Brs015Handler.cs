using System.Text.Json;
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

        // Build payload
        var payloadObj = new Dictionary<string, object?>
        {
            ["businessReason"] = "E34",
            ["gsrn"] = gsrn.Value,
            ["effectiveDate"] = effectiveDate,
            ["customerName"] = data.CustomerName,
        };
        if (data.Cpr is not null) payloadObj["cpr"] = data.Cpr;
        if (data.Cvr is not null) payloadObj["cvr"] = data.Cvr;
        if (data.Email is not null) payloadObj["email"] = data.Email;
        if (data.Phone is not null) payloadObj["phone"] = data.Phone;
        if (data.Address is not null)
        {
            payloadObj["address"] = new
            {
                streetName = data.Address.StreetName,
                buildingNumber = data.Address.BuildingNumber,
                postCode = data.Address.PostCode,
                cityName = data.Address.CityName,
                floor = data.Address.Floor,
                suite = data.Address.Suite
            };
        }

        var payload = JsonSerializer.Serialize(payloadObj);

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
