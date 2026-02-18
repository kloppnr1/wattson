namespace WattsOn.Domain.Enums;

/// <summary>
/// Our role in a BRS process — initiator or recipient.
/// Critical for state machine behavior: same process, different flows.
/// </summary>
public enum ProcessRole
{
    /// <summary>We initiated this process (e.g., requesting supplier change)</summary>
    Initiator = 1,

    /// <summary>We're receiving/responding to this process (e.g., losing a customer)</summary>
    Recipient = 2
}
