using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Entities;

public class SettlementTests
{
    private static readonly Guid MpId = Guid.NewGuid();
    private static readonly Guid LevId = Guid.NewGuid();
    private static readonly Guid TsId = Guid.NewGuid();
    private static readonly Period Jan2026 = Period.Create(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly EnergyQuantity Energy = EnergyQuantity.Create(1234.567m);

    private static Settlement CreateTestSettlement() =>
        Settlement.Create(MpId, LevId, Jan2026, TsId, 1, Energy);

    // --- Create ---

    [Fact]
    public void Create_SetsAllProperties()
    {
        var settlement = CreateTestSettlement();

        Assert.Equal(MpId, settlement.MeteringPointId);
        Assert.Equal(LevId, settlement.SupplyId);
        Assert.Equal(TsId, settlement.TimeSeriesId);
        Assert.Equal(1, settlement.TimeSeriesVersion);
        Assert.Equal(1234.567m, settlement.TotalEnergy.Value);
        Assert.Equal(Jan2026.Start, settlement.SettlementPeriod.Start);
        Assert.Equal(Jan2026.End, settlement.SettlementPeriod.End);
    }

    [Fact]
    public void Create_DefaultsToBeregnetStatus()
    {
        var settlement = CreateTestSettlement();

        Assert.Equal(SettlementStatus.Calculated, settlement.Status);
    }

    [Fact]
    public void Create_IsNotCorrection()
    {
        var settlement = CreateTestSettlement();

        Assert.False(settlement.IsCorrection);
        Assert.Null(settlement.PreviousSettlementId);
    }

    [Fact]
    public void Create_TotalAmountIsZero()
    {
        var settlement = CreateTestSettlement();

        Assert.Equal(0m, settlement.TotalAmount.Amount);
        Assert.Equal("DKK", settlement.TotalAmount.Currency);
    }

    [Fact]
    public void Create_SetsCalculatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var settlement = CreateTestSettlement();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(settlement.CalculatedAt, before, after);
    }

    [Fact]
    public void Create_NoInvoiceReference()
    {
        var settlement = CreateTestSettlement();

        Assert.Null(settlement.ExternalInvoiceReference);
        Assert.Null(settlement.InvoicedAt);
    }

    // --- CreateCorrection ---

    [Fact]
    public void CreateCorrection_SetsIsCorrectionTrue()
    {
        var originalId = Guid.NewGuid();
        var correction = Settlement.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 2, Energy, originalId);

        Assert.True(correction.IsCorrection);
        Assert.Equal(originalId, correction.PreviousSettlementId);
    }

    [Fact]
    public void CreateCorrection_DefaultsToBeregnet()
    {
        var correction = Settlement.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 2, Energy, Guid.NewGuid());

        Assert.Equal(SettlementStatus.Calculated, correction.Status);
    }

    [Fact]
    public void CreateCorrection_UsesNewTimeSeriesVersion()
    {
        var correction = Settlement.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 3, Energy, Guid.NewGuid());

        Assert.Equal(3, correction.TimeSeriesVersion);
    }

    // --- AddLine ---

    [Fact]
    public void AddLine_RecalculatesTotal()
    {
        var settlement = CreateTestSettlement();
        var line = SettlementLine.Create(
            settlement.Id, Guid.NewGuid(), "Nettarif", EnergyQuantity.Create(100m), 0.25m);

        settlement.AddLine(line);

        Assert.Equal(25.00m, settlement.TotalAmount.Amount);
    }

    [Fact]
    public void AddLine_MultipleLines_SumsCorrectly()
    {
        var settlement = CreateTestSettlement();

        settlement.AddLine(SettlementLine.Create(
            settlement.Id, Guid.NewGuid(), "Nettarif", EnergyQuantity.Create(100m), 0.25m));
        settlement.AddLine(SettlementLine.Create(
            settlement.Id, Guid.NewGuid(), "Systemtarif", EnergyQuantity.Create(100m), 0.10m));
        settlement.AddLine(SettlementLine.Create(
            settlement.Id, Guid.NewGuid(), "Elafgift", EnergyQuantity.Create(100m), 0.763m));

        // 25.00 + 10.00 + 76.30 = 111.30
        Assert.Equal(111.30m, settlement.TotalAmount.Amount);
        Assert.Equal(3, settlement.Lines.Count);
    }

    // --- MarkInvoiced ---

    [Fact]
    public void MarkInvoiced_FromBeregnet_Succeeds()
    {
        var settlement = CreateTestSettlement();

        settlement.MarkInvoiced("INV-2026-001");

        Assert.Equal(SettlementStatus.Invoiced, settlement.Status);
        Assert.Equal("INV-2026-001", settlement.ExternalInvoiceReference);
        Assert.NotNull(settlement.InvoicedAt);
    }

    [Fact]
    public void MarkInvoiced_SetsInvoicedAtTimestamp()
    {
        var settlement = CreateTestSettlement();
        var before = DateTimeOffset.UtcNow;

        settlement.MarkInvoiced("INV-2026-001");

        Assert.InRange(settlement.InvoicedAt!.Value, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void MarkInvoiced_FromFaktureret_Throws()
    {
        var settlement = CreateTestSettlement();
        settlement.MarkInvoiced("INV-2026-001");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            settlement.MarkInvoiced("INV-2026-002"));

        Assert.Contains("Invoiced", ex.Message);
    }

    [Fact]
    public void MarkInvoiced_FromJusteret_Throws()
    {
        var settlement = CreateTestSettlement();
        settlement.MarkInvoiced("INV-2026-001");
        settlement.MarkAdjusted();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            settlement.MarkInvoiced("INV-2026-002"));

        Assert.Contains("Adjusted", ex.Message);
    }

    // --- MarkAdjusted ---

    [Fact]
    public void MarkAdjusted_FromFaktureret_Succeeds()
    {
        var settlement = CreateTestSettlement();
        settlement.MarkInvoiced("INV-2026-001");

        settlement.MarkAdjusted();

        Assert.Equal(SettlementStatus.Adjusted, settlement.Status);
    }

    [Fact]
    public void MarkAdjusted_FromBeregnet_Throws()
    {
        var settlement = CreateTestSettlement();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            settlement.MarkAdjusted());

        Assert.Contains("Calculated", ex.Message);
    }

    [Fact]
    public void MarkAdjusted_FromJusteret_Throws()
    {
        var settlement = CreateTestSettlement();
        settlement.MarkInvoiced("INV-2026-001");
        settlement.MarkAdjusted();

        Assert.Throws<InvalidOperationException>(() =>
            settlement.MarkAdjusted());
    }

    [Fact]
    public void MarkAdjusted_PreservesInvoiceReference()
    {
        var settlement = CreateTestSettlement();
        settlement.MarkInvoiced("INV-2026-001");
        settlement.MarkAdjusted();

        Assert.Equal("INV-2026-001", settlement.ExternalInvoiceReference);
        Assert.NotNull(settlement.InvoicedAt);
    }

    // --- Full lifecycle ---

    [Fact]
    public void FullLifecycle_Beregnet_Faktureret_Justeret()
    {
        var settlement = CreateTestSettlement();
        Assert.Equal(SettlementStatus.Calculated, settlement.Status);

        settlement.MarkInvoiced("INV-2026-001");
        Assert.Equal(SettlementStatus.Invoiced, settlement.Status);

        settlement.MarkAdjusted();
        Assert.Equal(SettlementStatus.Adjusted, settlement.Status);
    }

    [Fact]
    public void CorrectionSettlement_FullFlow()
    {
        // Original settlement
        var original = CreateTestSettlement();
        original.AddLine(SettlementLine.Create(
            original.Id, Guid.NewGuid(), "Spotpris", EnergyQuantity.Create(500m), 1.50m));
        original.MarkInvoiced("INV-2026-001");

        // DataHub correction arrives — mark original as adjusted
        original.MarkAdjusted();

        // Create correction settlement with updated energy
        var newEnergy = EnergyQuantity.Create(520m); // 20 kWh more than original
        var correction = Settlement.CreateCorrection(
            MpId, LevId, Jan2026, TsId, 2, newEnergy, original.Id);
        correction.AddLine(SettlementLine.Create(
            correction.Id, Guid.NewGuid(), "Spotpris (justering)", EnergyQuantity.Create(20m), 1.50m));

        // Verify correction
        Assert.True(correction.IsCorrection);
        Assert.Equal(original.Id, correction.PreviousSettlementId);
        Assert.Equal(SettlementStatus.Calculated, correction.Status);
        Assert.Equal(30.00m, correction.TotalAmount.Amount); // 20 kWh × 1.50 DKK
    }
}

public class SettlementLineTests
{
    [Fact]
    public void Create_CalculatesAmount()
    {
        var line = SettlementLine.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Nettarif",
            EnergyQuantity.Create(150.5m), 0.3456m);

        // 150.5 × 0.3456 = 52.0128 → rounded to 52.01
        Assert.Equal(52.01m, line.Amount.Amount);
        Assert.Equal("DKK", line.Amount.Currency);
    }

    [Fact]
    public void Create_SetsDescription()
    {
        var line = SettlementLine.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Elafgift",
            EnergyQuantity.Create(100m), 0.763m);

        Assert.Equal("Elafgift", line.Description);
    }

    [Fact]
    public void Create_SetsQuantityAndUnitPrice()
    {
        var line = SettlementLine.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Systemtarif",
            EnergyQuantity.Create(250.123m), 0.054m);

        Assert.Equal(250.123m, line.Quantity.Value);
        Assert.Equal(0.054m, line.UnitPrice);
    }

    [Fact]
    public void Create_ZeroQuantity_ZeroAmount()
    {
        var line = SettlementLine.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Nettarif",
            EnergyQuantity.Create(0m), 0.25m);

        Assert.Equal(0m, line.Amount.Amount);
    }
}
