namespace WattsOn.Domain.Enums;

/// <summary>
/// Settlement method for a metering point.
/// DataHub 3 Phase 3: Profileret is being phased out — all move to Flex.
/// </summary>
public enum SettlementMethod
{
    /// <summary>D01 — Flex settled (hourly based on actual meter readings)</summary>
    Flex = 1,

    /// <summary>E02 — Non-profiled (settled on actual readings, typically large consumers)</summary>
    IkkeProfileret = 2,

    /// <summary>D01 legacy — Profiled (settled on standard load profiles, being phased out)</summary>
    Profileret = 3
}
