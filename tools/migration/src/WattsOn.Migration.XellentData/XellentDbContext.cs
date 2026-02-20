using Microsoft.EntityFrameworkCore;
using WattsOn.Migration.XellentData.Entities;

namespace WattsOn.Migration.XellentData;

public class XellentDbContext : DbContext
{
    public XellentDbContext(DbContextOptions<XellentDbContext> options) : base(options) { }

    public DbSet<CustTable> CustTables => Set<CustTable>();
    public DbSet<ExuContractTable> ExuContractTables => Set<ExuContractTable>();
    public DbSet<ExuContractPartTable> ExuContractPartTables => Set<ExuContractPartTable>();
    public DbSet<ExuAgreementTable> ExuAgreementTables => Set<ExuAgreementTable>();
    public DbSet<ExuDelpoint> ExuDelpoints => Set<ExuDelpoint>();
    public DbSet<ExuRateTable> ExuRateTables => Set<ExuRateTable>();
    public DbSet<EmsTimeseries> EmsTimeseries => Set<EmsTimeseries>();
    public DbSet<EmsTimeseriesValues> EmsTimeseriesValues => Set<EmsTimeseriesValues>();
    public DbSet<FlexBillingHistoryTable> FlexBillingHistoryTables => Set<FlexBillingHistoryTable>();
    public DbSet<FlexBillingHistoryLine> FlexBillingHistoryLines => Set<FlexBillingHistoryLine>();
    public DbSet<PriceElementCheck> PriceElementChecks => Set<PriceElementCheck>();
    public DbSet<PriceElementCheckData> PriceElementCheckData => Set<PriceElementCheckData>();
    public DbSet<PriceElementTable> PriceElementTables => Set<PriceElementTable>();
    public DbSet<PriceElementRates> PriceElementRates => Set<PriceElementRates>();
    public DbSet<ExuProductExtendTable> ExuProductExtendTables => Set<ExuProductExtendTable>();
    public DbSet<InventTable> InventTables => Set<InventTable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(XellentDbContext).Assembly);
    }
}
