using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Målepunkt — a metering point in the electricity grid.
/// The fundamental unit of measurement and settlement.
/// Identified by an 18-digit GSRN number.
/// </summary>
public class Målepunkt : Entity
{
    public Gsrn Gsrn { get; private set; } = null!;
    public MålepunktsType Type { get; private set; }
    public MålepunktsArt Art { get; private set; }
    public SettlementMethod SettlementMethod { get; private set; }
    public Resolution Resolution { get; private set; }
    public ConnectionState ConnectionState { get; private set; }
    public Address? Address { get; private set; }

    /// <summary>Grid area code (netområde)</summary>
    public string GridArea { get; private set; } = null!;

    /// <summary>GLN of the grid company responsible for this metering point</summary>
    public GlnNumber GridCompanyGln { get; private set; } = null!;

    /// <summary>Whether we are the current supplier for this metering point</summary>
    public bool HasActiveSupply { get; private set; }

    /// <summary>Supply agreements for this metering point</summary>
    private readonly List<Leverance> _leverancer = new();
    public IReadOnlyList<Leverance> Leverancer => _leverancer.AsReadOnly();

    /// <summary>Time series data for this metering point</summary>
    private readonly List<Tidsserie> _tidsserier = new();
    public IReadOnlyList<Tidsserie> Tidsserier => _tidsserier.AsReadOnly();

    private Målepunkt() { } // EF Core

    public static Målepunkt Create(
        Gsrn gsrn,
        MålepunktsType type,
        MålepunktsArt art,
        SettlementMethod settlementMethod,
        Resolution resolution,
        string gridArea,
        GlnNumber gridCompanyGln,
        Address? address = null)
    {
        return new Målepunkt
        {
            Gsrn = gsrn,
            Type = type,
            Art = art,
            SettlementMethod = settlementMethod,
            Resolution = resolution,
            ConnectionState = ConnectionState.Tilsluttet,
            GridArea = gridArea,
            GridCompanyGln = gridCompanyGln,
            Address = address,
            HasActiveSupply = false
        };
    }

    public void UpdateConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        MarkUpdated();
    }

    public void UpdateSettlementMethod(SettlementMethod method)
    {
        SettlementMethod = method;
        MarkUpdated();
    }

    public void SetActiveSupply(bool active)
    {
        HasActiveSupply = active;
        MarkUpdated();
    }

    public void UpdateAddress(Address address)
    {
        Address = address;
        MarkUpdated();
    }
}
