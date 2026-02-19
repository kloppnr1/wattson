using Microsoft.EntityFrameworkCore;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class InboxOutboxEndpoints
{
    public static WebApplication MapInboxOutboxEndpoints(this WebApplication app)
    {
        app.MapGet("/api/inbox", async (WattsOnDbContext db, bool? unprocessed) =>
        {
            var query = db.InboxMessages.AsNoTracking();
            if (unprocessed == true)
                query = query.Where(m => !m.IsProcessed);

            var messages = await query
                .OrderByDescending(m => m.ReceivedAt)
                .Take(100)
                .Select(m => new
                {
                    m.Id,
                    m.MessageId,
                    m.DocumentType,
                    m.BusinessProcess,
                    m.SenderGln,
                    m.ReceiverGln,
                    m.ReceivedAt,
                    m.IsProcessed,
                    m.ProcessedAt,
                    m.ProcessingError,
                    m.ProcessingAttempts
                })
                .ToListAsync();
            return Results.Ok(messages);
        }).WithName("GetInbox");

        app.MapGet("/api/outbox", async (WattsOnDbContext db, bool? unsent) =>
        {
            var query = db.OutboxMessages.AsNoTracking();
            if (unsent == true)
                query = query.Where(m => !m.IsSent);

            var messages = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(100)
                .Select(m => new
                {
                    m.Id,
                    m.DocumentType,
                    m.BusinessProcess,
                    m.SenderGln,
                    m.ReceiverGln,
                    m.IsSent,
                    m.SentAt,
                    m.SendError,
                    m.SendAttempts,
                    m.ScheduledFor,
                    m.CreatedAt
                })
                .ToListAsync();
            return Results.Ok(messages);
        }).WithName("GetOutbox");

        return app;
    }
}
