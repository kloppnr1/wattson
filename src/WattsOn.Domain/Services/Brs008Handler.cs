using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-008 — Tilslutning af målepunkt (Connection of Metering Point).
/// Grid company physically connects a newly created MP.
/// Updates connection state from Ny → Tilsluttet.
/// Pure domain logic — no persistence.
/// </summary>
public static class Brs008Handler
{
    public record ConnectionResult(
        MeteringPoint MeteringPoint,
        ConnectionState PreviousState,
        bool WasChanged);

    /// <summary>
    /// Update the connection state of a metering point to Tilsluttet (connected).
    /// </summary>
    public static ConnectionResult Connect(MeteringPoint mp, ConnectionState newState = ConnectionState.Tilsluttet)
    {
        var previousState = mp.ConnectionState;

        if (previousState == newState)
        {
            return new ConnectionResult(mp, previousState, WasChanged: false);
        }

        mp.UpdateConnectionState(newState);
        return new ConnectionResult(mp, previousState, WasChanged: true);
    }
}
