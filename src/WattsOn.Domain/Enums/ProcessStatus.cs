namespace WattsOn.Domain.Enums;

/// <summary>
/// General status for BRS processes.
/// Each process type has its own specific states via state machines,
/// but this provides a high-level lifecycle status.
/// </summary>
public enum ProcessStatus
{
    /// <summary>Process created, not yet submitted or received</summary>
    Oprettet = 1,

    /// <summary>Request submitted to DataHub, awaiting confirmation</summary>
    Indsendt = 2,

    /// <summary>Received from DataHub, awaiting processing</summary>
    Modtaget = 3,

    /// <summary>DataHub confirmed receipt / accepted</summary>
    Bekræftet = 4,

    /// <summary>Process is active and in progress</summary>
    IgangVærende = 5,

    /// <summary>Process completed successfully</summary>
    Gennemført = 6,

    /// <summary>Process was rejected by DataHub or counterpart</summary>
    Afvist = 7,

    /// <summary>Process was cancelled</summary>
    Annulleret = 8,

    /// <summary>Process failed with error</summary>
    Fejlet = 9
}
