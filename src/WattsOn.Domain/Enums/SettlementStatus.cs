namespace WattsOn.Domain.Enums;

/// <summary>
/// Status of a settlement in the invoicing lifecycle.
/// WattsOn calculates settlements; an external system handles invoicing.
/// </summary>
public enum SettlementStatus
{
    /// <summary>Calculated and ready to be picked up by external invoicing system</summary>
    Calculated = 1,

    /// <summary>External system has confirmed this settlement is invoiced</summary>
    Invoiced = 2,

    /// <summary>A DataHub correction invalidated this settlement — adjustment created</summary>
    Adjusted = 3
}
