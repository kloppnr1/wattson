namespace WattsOn.Domain.Enums;

/// <summary>
/// Market participant role in the Danish electricity market.
/// Maps to DataHub role codes.
/// </summary>
public enum ActorRole
{
    /// <summary>DDQ — Elleverandør (Electricity Supplier)</summary>
    Elleverandør = 1,

    /// <summary>DDM — Netvirksomhed (Grid Company / DSO)</summary>
    Netvirksomhed = 2,

    /// <summary>DDZ — Balanceansvarlig (Balance Responsible Party)</summary>
    Balanceansvarlig = 3,

    /// <summary>DDX — Balancesettlementsansvarlig (Balance Settlement Responsible)</summary>
    Balancesettlementsansvarlig = 4,

    /// <summary>DGL — DataHub (Energinet)</summary>
    DataHub = 5,

    /// <summary>EZ — TSO (Energinet as Transmission System Operator)</summary>
    TSO = 6,

    /// <summary>STS — Energistyrelsen (Danish Energy Agency)</summary>
    Energistyrelsen = 7
}
