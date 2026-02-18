namespace WattsOn.Domain.Enums;

/// <summary>
/// Actor roles in the Danish electricity market.
/// Maps to DataHub actor role codes.
/// </summary>
public enum ActorRole
{
    /// <summary>DDQ — Electricity Supplier (Elleverandør)</summary>
    Supplier = 1,

    /// <summary>DDM — Grid Company (Netvirksomhed)</summary>
    GridCompany = 2,

    /// <summary>DDK — Balance Responsible Party (Balanceansvarlig)</summary>
    BalanceResponsible = 3,

    /// <summary>DDX — Balance Settlement Responsible (Balancesettlementsansvarlig)</summary>
    BalanceSettlementResponsible = 4,

    /// <summary>DGL — DataHub (Energinet)</summary>
    DataHub = 5,

    /// <summary>TSO (Energinet Transmission)</summary>
    TSO = 6,

    /// <summary>STS — Danish Energy Agency (Energistyrelsen)</summary>
    EnergyAgency = 7
}
