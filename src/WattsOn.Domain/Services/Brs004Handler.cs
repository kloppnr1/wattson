using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-004 — Oprettelse af målepunkt (New Metering Point Created).
/// Grid company creates a new metering point and DataHub notifies us.
/// We upsert the metering point in our database.
/// Pure domain logic — no persistence.
/// </summary>
public static class Brs004Handler
{
    public record NewMeteringPointData(
        Gsrn Gsrn,
        MeteringPointType Type,
        MeteringPointCategory Art,
        SettlementMethod SettlementMethod,
        Resolution Resolution,
        string GridArea,
        GlnNumber GridCompanyGln,
        ConnectionState ConnectionState,
        Address? Address = null,
        Gsrn? ParentGsrn = null);

    public record CreateResult(MeteringPoint MeteringPoint, bool WasCreated, List<string> ChangedFields);

    /// <summary>
    /// Create a new metering point from the BRS-004 payload.
    /// </summary>
    public static CreateResult CreateMeteringPoint(NewMeteringPointData data)
    {
        var mp = MeteringPoint.Create(
            data.Gsrn,
            data.Type,
            data.Art,
            data.SettlementMethod,
            data.Resolution,
            data.GridArea,
            data.GridCompanyGln,
            data.Address);

        // BRS-004 creates new MPs — they start in the specified connection state (usually Ny)
        mp.UpdateConnectionState(data.ConnectionState);

        var changed = new List<string>
        {
            "Type", "Art", "SettlementMethod", "Resolution",
            "ConnectionState", "GridArea", "GridCompanyGln"
        };
        if (data.Address is not null) changed.Add("Address");

        return new CreateResult(mp, WasCreated: true, changed);
    }

    /// <summary>
    /// Update an existing metering point with data from BRS-004.
    /// Uses the BRS-006 pattern for field-by-field updates.
    /// </summary>
    public static CreateResult UpdateExistingMeteringPoint(MeteringPoint mp, NewMeteringPointData data)
    {
        var update = new Brs006Handler.MasterDataUpdate(
            Type: data.Type,
            Art: data.Art,
            SettlementMethod: data.SettlementMethod,
            Resolution: data.Resolution,
            ConnectionState: data.ConnectionState,
            GridArea: data.GridArea,
            GridCompanyGln: data.GridCompanyGln,
            Address: data.Address);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        return new CreateResult(mp, WasCreated: false, result.ChangedFields);
    }
}
