using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Entities;

public class AfregningTests
{
    private static readonly Guid MpId = Guid.NewGuid();
    private static readonly Guid LevId = Guid.NewGuid();
    private static readonly Guid TsId = Guid.NewGuid();
    private static readonly Period Jan2026 = Period.Create(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly EnergyQuantity Energy = EnergyQuantity.Create(1234.567m);

    private static Afregning CreateTestAfregning() =>
        Afregning.Create(MpId, LevId, Jan2026, TsId, 1, Energy);

    // --- Create ---

    [Fact]
    public void Create_SetsAllProperties()
    {
        var afregning = CreateTestAfregning();

        Assert.Equal(MpId, afregning.MålepunktId);
        Assert.Equal(LevId, afregning.LeveranceId);
        Assert.Equal(TsId, afregning.TidsserieId);
        Assert.Equal(1, afregning.TidsserieVersion);
        Assert.Equal(1234.567m, afregning.TotalEnergy.Value);
        Assert.Equal(Jan2026.Start, afregning.SettlementPeriod.Start);
        Assert.Equal(Jan2026.End, afregning.SettlementPeriod.End);
    }

    [Fact]
    public void Create_DefaultsToBeregnetStatus()
    {
        var afregning = CreateTestAfregning();

        Assert.Equal(AfregningStatus.Beregnet, afregning.Status);
    }

    [Fact]
    public void Create_IsNotCorrection()
    {
        var afregning = CreateTestAfregning();

        Assert.False(afregning.IsCorrection);
        Assert.Null(afregning.PreviousAfregningId);
    }

    [Fact]
    public void Create_TotalAmountIsZero()
    {
        var afregning = CreateTestAfregning();

        Assert.Equal(0m, afregning.TotalAmount.Amount);
        Assert.Equal("DKK", afregning.TotalAmount.Currency);
    }

    [Fact]
    public void Create_SetsCalculatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var afregning = CreateTestAfregning();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(afregning.CalculatedAt, before, after);
    }

    [Fact]
    public void Create_NoInvoiceReference()
    {
        var afregning = CreateTestAfregning();

        Assert.Null(afregning.ExternalInvoiceReference);
        Assert.Null(afregning.InvoicedAt);
    }

    // --- CreateCorrection ---

    [Fact]
    public void CreateCorrection_SetsIsCorrectionTrue()
    {
        var originalId = Guid.NewGuid();
        var correction = Afregning.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 2, Energy, originalId);

        Assert.True(correction.IsCorrection);
        Assert.Equal(originalId, correction.PreviousAfregningId);
    }

    [Fact]
    public void CreateCorrection_DefaultsToBeregnet()
    {
        var correction = Afregning.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 2, Energy, Guid.NewGuid());

        Assert.Equal(AfregningStatus.Beregnet, correction.Status);
    }

    [Fact]
    public void CreateCorrection_UsesNewTidsserieVersion()
    {
        var correction = Afregning.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 3, Energy, Guid.NewGuid());

        Assert.Equal(3, correction.TidsserieVersion);
    }

    // --- AddLine ---

    [Fact]
    public void AddLine_RecalculatesTotal()
    {
        var afregning = CreateTestAfregning();
        var line = AfregningLinje.Create(
            afregning.Id, Guid.NewGuid(), "Nettarif", EnergyQuantity.Create(100m), 0.25m);

        afregning.AddLine(line);

        Assert.Equal(25.00m, afregning.TotalAmount.Amount);
    }

    [Fact]
    public void AddLine_MultipleLines_SumsCorrectly()
    {
        var afregning = CreateTestAfregning();

        afregning.AddLine(AfregningLinje.Create(
            afregning.Id, Guid.NewGuid(), "Nettarif", EnergyQuantity.Create(100m), 0.25m));
        afregning.AddLine(AfregningLinje.Create(
            afregning.Id, Guid.NewGuid(), "Systemtarif", EnergyQuantity.Create(100m), 0.10m));
        afregning.AddLine(AfregningLinje.Create(
            afregning.Id, Guid.NewGuid(), "Elafgift", EnergyQuantity.Create(100m), 0.763m));

        // 25.00 + 10.00 + 76.30 = 111.30
        Assert.Equal(111.30m, afregning.TotalAmount.Amount);
        Assert.Equal(3, afregning.Lines.Count);
    }

    // --- MarkInvoiced ---

    [Fact]
    public void MarkInvoiced_FromBeregnet_Succeeds()
    {
        var afregning = CreateTestAfregning();

        afregning.MarkInvoiced("INV-2026-001");

        Assert.Equal(AfregningStatus.Faktureret, afregning.Status);
        Assert.Equal("INV-2026-001", afregning.ExternalInvoiceReference);
        Assert.NotNull(afregning.InvoicedAt);
    }

    [Fact]
    public void MarkInvoiced_SetsInvoicedAtTimestamp()
    {
        var afregning = CreateTestAfregning();
        var before = DateTimeOffset.UtcNow;

        afregning.MarkInvoiced("INV-2026-001");

        Assert.InRange(afregning.InvoicedAt!.Value, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void MarkInvoiced_FromFaktureret_Throws()
    {
        var afregning = CreateTestAfregning();
        afregning.MarkInvoiced("INV-2026-001");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            afregning.MarkInvoiced("INV-2026-002"));

        Assert.Contains("Faktureret", ex.Message);
    }

    [Fact]
    public void MarkInvoiced_FromJusteret_Throws()
    {
        var afregning = CreateTestAfregning();
        afregning.MarkInvoiced("INV-2026-001");
        afregning.MarkAdjusted();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            afregning.MarkInvoiced("INV-2026-002"));

        Assert.Contains("Justeret", ex.Message);
    }

    // --- MarkAdjusted ---

    [Fact]
    public void MarkAdjusted_FromFaktureret_Succeeds()
    {
        var afregning = CreateTestAfregning();
        afregning.MarkInvoiced("INV-2026-001");

        afregning.MarkAdjusted();

        Assert.Equal(AfregningStatus.Justeret, afregning.Status);
    }

    [Fact]
    public void MarkAdjusted_FromBeregnet_Throws()
    {
        var afregning = CreateTestAfregning();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            afregning.MarkAdjusted());

        Assert.Contains("Beregnet", ex.Message);
    }

    [Fact]
    public void MarkAdjusted_FromJusteret_Throws()
    {
        var afregning = CreateTestAfregning();
        afregning.MarkInvoiced("INV-2026-001");
        afregning.MarkAdjusted();

        Assert.Throws<InvalidOperationException>(() =>
            afregning.MarkAdjusted());
    }

    [Fact]
    public void MarkAdjusted_PreservesInvoiceReference()
    {
        var afregning = CreateTestAfregning();
        afregning.MarkInvoiced("INV-2026-001");
        afregning.MarkAdjusted();

        Assert.Equal("INV-2026-001", afregning.ExternalInvoiceReference);
        Assert.NotNull(afregning.InvoicedAt);
    }

    // --- Full lifecycle ---

    [Fact]
    public void FullLifecycle_Beregnet_Faktureret_Justeret()
    {
        var afregning = CreateTestAfregning();
        Assert.Equal(AfregningStatus.Beregnet, afregning.Status);

        afregning.MarkInvoiced("INV-2026-001");
        Assert.Equal(AfregningStatus.Faktureret, afregning.Status);

        afregning.MarkAdjusted();
        Assert.Equal(AfregningStatus.Justeret, afregning.Status);
    }

    [Fact]
    public void CorrectionSettlement_FullFlow()
    {
        // Original settlement
        var original = CreateTestAfregning();
        original.AddLine(AfregningLinje.Create(
            original.Id, Guid.NewGuid(), "Spotpris", EnergyQuantity.Create(500m), 1.50m));
        original.MarkInvoiced("INV-2026-001");

        // DataHub correction arrives — mark original as adjusted
        original.MarkAdjusted();

        // Create correction settlement with updated energy
        var newEnergy = EnergyQuantity.Create(520m); // 20 kWh more than original
        var correction = Afregning.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 2, newEnergy, original.Id);
        correction.AddLine(AfregningLinje.Create(
            correction.Id, Guid.NewGuid(), "Spotpris (justering)", EnergyQuantity.Create(20m), 1.50m));

        // Verify correction
        Assert.True(correction.IsCorrection);
        Assert.Equal(original.Id, correction.PreviousAfregningId);
        Assert.Equal(AfregningStatus.Beregnet, correction.Status);
        Assert.Equal(30.00m, correction.TotalAmount.Amount); // 20 kWh × 1.50 DKK
    }
}

public class AfregningLinjeTests
{
    [Fact]
    public void Create_CalculatesAmount()
    {
        var line = AfregningLinje.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Nettarif",
            EnergyQuantity.Create(150.5m), 0.3456m);

        // 150.5 × 0.3456 = 52.0128 → rounded to 52.01
        Assert.Equal(52.01m, line.Amount.Amount);
        Assert.Equal("DKK", line.Amount.Currency);
    }

    [Fact]
    public void Create_SetsDescription()
    {
        var line = AfregningLinje.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Elafgift",
            EnergyQuantity.Create(100m), 0.763m);

        Assert.Equal("Elafgift", line.Description);
    }

    [Fact]
    public void Create_SetsQuantityAndUnitPrice()
    {
        var line = AfregningLinje.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Systemtarif",
            EnergyQuantity.Create(250.123m), 0.054m);

        Assert.Equal(250.123m, line.Quantity.Value);
        Assert.Equal(0.054m, line.UnitPrice);
    }

    [Fact]
    public void Create_ZeroQuantity_ZeroAmount()
    {
        var line = AfregningLinje.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Nettarif",
            EnergyQuantity.Create(0m), 0.25m);

        Assert.Equal(0m, line.Amount.Amount);
    }
}
