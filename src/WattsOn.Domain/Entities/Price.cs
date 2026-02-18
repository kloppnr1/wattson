using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Price — a price/charge from a grid company or other party.
/// Received via BRS-031 (price updates) or BRS-034 (price requests).
/// Prices have validity periods and can have time-varying rates (e.g., hourly tariffs).
/// </summary>
public class Price : Entity
{
    /// <summary>Charge type ID from DataHub</summary>
    public string ChargeId { get; private set; } = null!;

    /// <summary>GLN of the charge owner (grid company, TSO, etc.)</summary>
    public GlnNumber OwnerGln { get; private set; } = null!;

    /// <summary>Type of charge</summary>
    public PriceType Type { get; private set; }

    /// <summary>Human-readable description</summary>
    public string Description { get; private set; } = null!;

    /// <summary>Period during which this price is valid</summary>
    public Period ValidityPeriod { get; private set; } = null!;

    /// <summary>Whether this price is VAT-exempt</summary>
    public bool VatExempt { get; private set; }

    /// <summary>Resolution for time-varying prices (null for fixed)</summary>
    public Resolution? PriceResolution { get; private set; }

    /// <summary>Price points (for tariffs: one per hour/quarter-hour)</summary>
    private readonly List<PricePoint> _pricePoints = new();
    public IReadOnlyList<PricePoint> PricePoints => _pricePoints.AsReadOnly();

    private Price() { } // EF Core

    public static Price Create(
        string chargeId,
        GlnNumber ownerGln,
        PriceType type,
        string description,
        Period validityPeriod,
        bool vatExempt = false,
        Resolution? priceResolution = null)
    {
        return new Price
        {
            ChargeId = chargeId,
            OwnerGln = ownerGln,
            Type = type,
            Description = description,
            ValidityPeriod = validityPeriod,
            VatExempt = vatExempt,
            PriceResolution = priceResolution
        };
    }

    public void AddPricePoint(DateTimeOffset timestamp, decimal price)
    {
        _pricePoints.Add(PricePoint.Create(Id, timestamp, price));
    }

    /// <summary>Get the price at a specific point in time</summary>
    public decimal? GetPriceAt(DateTimeOffset timestamp)
    {
        if (Type == PriceType.Abonnement)
        {
            // Subscriptions: return the single price point (monthly amount)
            return _pricePoints.FirstOrDefault()?.Price;
        }

        // Tariffs: find the price point that matches the timestamp
        return _pricePoints
            .Where(pp => pp.Timestamp <= timestamp)
            .OrderByDescending(pp => pp.Timestamp)
            .FirstOrDefault()?.Price;
    }
}

/// <summary>
/// A single price point (rate at a specific time).
/// </summary>
public class PricePoint : Entity
{
    public Guid PriceId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public decimal Price { get; private set; }

    private PricePoint() { } // EF Core

    public static PricePoint Create(Guid priceId, DateTimeOffset timestamp, decimal price)
    {
        return new PricePoint
        {
            PriceId = priceId,
            Timestamp = timestamp,
            Price = price
        };
    }
}
