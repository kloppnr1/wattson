using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-013 — Afbrydelse/Gentilslutning (Disconnect/Reconnect).
/// Grid company physically disconnects or reconnects a metering point.
/// Updates connection state to Afbrudt or Tilsluttet.
/// Pure domain logic — no persistence.
/// </summary>
public static class Brs013Handler
{
    public record ConnectionStateChangeResult(
        MeteringPoint MeteringPoint,
        ConnectionState PreviousState,
        ConnectionState NewState,
        bool WasChanged);

    /// <summary>
    /// Parse a connection state string from the DataHub payload.
    /// Returns null for unrecognized values.
    /// </summary>
    public static ConnectionState? ParseConnectionState(string? stateStr)
    {
        if (string.IsNullOrEmpty(stateStr)) return null;

        if (Enum.TryParse<ConnectionState>(stateStr, ignoreCase: true, out var state))
            return state;

        return null;
    }

    /// <summary>
    /// Update the connection state of a metering point (disconnect or reconnect).
    /// </summary>
    public static ConnectionStateChangeResult UpdateConnectionState(MeteringPoint mp, ConnectionState newState)
    {
        var previousState = mp.ConnectionState;

        if (previousState == newState)
        {
            return new ConnectionStateChangeResult(mp, previousState, newState, WasChanged: false);
        }

        mp.UpdateConnectionState(newState);
        return new ConnectionStateChangeResult(mp, previousState, newState, WasChanged: true);
    }
}
