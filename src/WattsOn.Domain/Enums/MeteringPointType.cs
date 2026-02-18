namespace WattsOn.Domain.Enums;

/// <summary>
/// MeteringPointstype — the role/type of a metering point in the grid.
/// Maps to DataHub E17/E18/E20/D01-D99 code list.
/// </summary>
public enum MeteringPointType
{
    /// <summary>E17 — Consumption (forbrug)</summary>
    Forbrug = 1,

    /// <summary>E18 — Production (produktion)</summary>
    Produktion = 2,

    /// <summary>E20 — Exchange (udveksling mellem netområder)</summary>
    Udveksling = 3,

    /// <summary>D01 — Net production (nettoproduktion)</summary>
    NettoProduktion = 4,

    /// <summary>D02 — Net from grid (netto fra net)</summary>
    NettoFraNet = 5,

    /// <summary>D04 — Supply to grid (levering til net)</summary>
    LeveringTilNet = 6,

    /// <summary>D05 — Net consumption (nettoforbrug)</summary>
    NettoForbrug = 7,

    /// <summary>D06 — Own production supply (egenproduktion i forsyning)</summary>
    EgenProduktionIForsyning = 8,

    /// <summary>D07 — Net production from grid (nettoproduktion fra net)</summary>
    NettoProduktionFraNet = 9,

    /// <summary>D08 — Excess production (overskudsproduktion)</summary>
    OverskudsProduktion = 10,

    /// <summary>D09 — Own production (egenproduktion)</summary>
    EgenProduktion = 11,

    /// <summary>D14 — Electrical heating (elvarme)</summary>
    ElVarme = 12,

    /// <summary>D15 — Net consumption calculated (nettoforbrug, beregnet)</summary>
    NettoForbrugBeregnet = 13
}
