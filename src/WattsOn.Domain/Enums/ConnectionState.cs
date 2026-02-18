namespace WattsOn.Domain.Enums;

/// <summary>
/// Physical connection state of a metering point.
/// </summary>
public enum ConnectionState
{
    /// <summary>D03 — Connected to grid and active</summary>
    Tilsluttet = 1,

    /// <summary>D04 — Disconnected from grid</summary>
    Afbrudt = 2,

    /// <summary>E22 — New/planned, not yet connected</summary>
    Ny = 3,

    /// <summary>E23 — Closed/decommissioned</summary>
    Nedlagt = 4
}
