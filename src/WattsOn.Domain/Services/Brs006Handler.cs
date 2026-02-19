using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-006 — Opdatering af målepunktsstamdata.
/// Processes RSM-022/E32 notifications from DataHub about metering point master data changes.
/// As a supplier, we receive updates about metering points where we have active supply.
/// Pure domain logic — no persistence.
/// </summary>
public static class Brs006Handler
{
    public record MasterDataUpdate(
        MeteringPointType? Type,
        MeteringPointCategory? Art,
        SettlementMethod? SettlementMethod,
        Resolution? Resolution,
        ConnectionState? ConnectionState,
        string? GridArea,
        GlnNumber? GridCompanyGln,
        Address? Address);

    public record UpdateResult(MeteringPoint MeteringPoint, List<string> ChangedFields);

    /// <summary>
    /// Apply master data updates to a metering point.
    /// Only non-null fields in the update are applied.
    /// Returns the updated metering point and a list of what changed.
    /// </summary>
    public static UpdateResult ApplyMasterDataUpdate(MeteringPoint mp, MasterDataUpdate update)
    {
        var changed = new List<string>();

        if (update.Type.HasValue && update.Type.Value != mp.Type)
        {
            mp.UpdateType(update.Type.Value);
            changed.Add("Type");
        }

        if (update.Art.HasValue && update.Art.Value != mp.Art)
        {
            mp.UpdateCategory(update.Art.Value);
            changed.Add("Art");
        }

        if (update.SettlementMethod.HasValue && update.SettlementMethod.Value != mp.SettlementMethod)
        {
            mp.UpdateSettlementMethod(update.SettlementMethod.Value);
            changed.Add("SettlementMethod");
        }

        if (update.Resolution.HasValue && update.Resolution.Value != mp.Resolution)
        {
            mp.UpdateResolution(update.Resolution.Value);
            changed.Add("Resolution");
        }

        if (update.ConnectionState.HasValue && update.ConnectionState.Value != mp.ConnectionState)
        {
            mp.UpdateConnectionState(update.ConnectionState.Value);
            changed.Add("ConnectionState");
        }

        if (update.GridArea is not null && update.GridArea != mp.GridArea)
        {
            var gln = update.GridCompanyGln ?? mp.GridCompanyGln;
            mp.UpdateGridArea(update.GridArea, gln);
            changed.Add("GridArea");
        }

        if (update.Address is not null)
        {
            mp.UpdateAddress(update.Address);
            changed.Add("Address");
        }

        return new UpdateResult(mp, changed);
    }
}
