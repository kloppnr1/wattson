using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs036HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571234567890123456");
    private static readonly DateTimeOffset EffectiveDate = DateTimeOffset.UtcNow.AddDays(1);

    [Fact]
    public void RecordObligationChange_CreatesAuditProcess()
    {
        var data = new Brs036Handler.ProductObligationData(TestGsrn, true, EffectiveDate);

        var result = Brs036Handler.RecordObligationChange(data, meteringPointFound: true);

        Assert.NotNull(result.Process);
        Assert.Equal(ProcessType.AftagepligtÆndring, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Recipient, result.Process.Role);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal(TestGsrn, result.Process.MeteringPointGsrn);
        Assert.Equal(EffectiveDate, result.Process.EffectiveDate);
    }

    [Fact]
    public void RecordObligationChange_RecordsObligationStatus_InProcessData()
    {
        var dataSet = new Brs036Handler.ProductObligationData(TestGsrn, true, EffectiveDate);
        var dataRemoved = new Brs036Handler.ProductObligationData(TestGsrn, false, EffectiveDate);

        var resultSet = Brs036Handler.RecordObligationChange(dataSet, meteringPointFound: true);
        var resultRemoved = Brs036Handler.RecordObligationChange(dataRemoved, meteringPointFound: true);

        // Process data should contain obligation details
        Assert.NotNull(resultSet.Process.ProcessData);
        Assert.Contains("true", resultSet.Process.ProcessData, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(resultRemoved.Process.ProcessData);
        Assert.Contains("false", resultRemoved.Process.ProcessData, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordObligationChange_HandlesMissingMp_Gracefully()
    {
        var data = new Brs036Handler.ProductObligationData(TestGsrn, true, EffectiveDate);

        var result = Brs036Handler.RecordObligationChange(data, meteringPointFound: false);

        // Should not throw, should still create process
        Assert.NotNull(result.Process);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.False(result.MeteringPointFound);
        Assert.NotNull(result.Process.ProcessData);
        Assert.Contains("false", result.Process.ProcessData); // MeteringPointFound: false
    }
}
