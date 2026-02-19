using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class DashboardEndpoints
{
    public static WebApplication MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dashboard", async (WattsOnDbContext db) =>
        {
            var customerCount = await db.Customers.CountAsync();
            var mpCount = await db.MeteringPoints.CountAsync();
            var activeSupplies = await db.Supplies.CountAsync(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > DateTimeOffset.UtcNow);
            var activeProcesses = await db.Processes.CountAsync(p => p.CompletedAt == null);
            var unprocessedInbox = await db.InboxMessages.CountAsync(m => !m.IsProcessed);
            var unsentOutbox = await db.OutboxMessages.CountAsync(m => !m.IsSent);

            // Settlement stats
            var beregnede = await db.Settlements.CountAsync(a => a.Status == SettlementStatus.Calculated && !a.IsCorrection);
            var fakturerede = await db.Settlements.CountAsync(a => a.Status == SettlementStatus.Invoiced);
            var justerede = await db.Settlements.CountAsync(a => a.Status == SettlementStatus.Adjusted);
            var korrektioner = await db.Settlements.CountAsync(a => a.IsCorrection && a.Status == SettlementStatus.Calculated);
            var totalSettlementAmount = await db.Settlements
                .Where(a => !a.IsCorrection)
                .SumAsync(a => a.TotalAmount.Amount);

            // Settlement issues
            var openIssues = await db.SettlementIssues.CountAsync(i => i.Status == SettlementIssueStatus.Open);

            return Results.Ok(new
            {
                customers = customerCount,
                meteringPoints = mpCount,
                activeSupplies = activeSupplies,
                activeProcesses = activeProcesses,
                unprocessedInbox = unprocessedInbox,
                unsentOutbox = unsentOutbox,
                settlements = new
                {
                    calculated = beregnede,
                    invoiced = fakturerede,
                    adjusted = justerede,
                    corrections = korrektioner,
                    totalAmount = totalSettlementAmount,
                    blockedIssues = openIssues,
                }
            });
        }).WithName("GetDashboard");

        return app;
    }
}
