namespace WattsOn.Domain.Enums;

/// <summary>
/// Status of a settlement in the invoicing lifecycle.
/// WattsOn calculates settlements; an external system handles invoicing.
/// </summary>
public enum AfregningStatus
{
    /// <summary>Calculated and ready to be picked up by external invoicing system</summary>
    Beregnet = 1,

    /// <summary>External system has confirmed this settlement is invoiced</summary>
    Faktureret = 2,

    /// <summary>A DataHub correction invalidated this settlement — adjustment created</summary>
    Justeret = 3
}
