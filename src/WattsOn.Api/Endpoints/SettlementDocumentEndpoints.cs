using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Enums;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SettlementDocumentEndpoints
{
    public static WebApplication MapSettlementDocumentEndpoints(this WebApplication app)
    {
        /// <summary>
        /// Settlement documents ready for external invoicing system.
        /// Returns Peppol BIS-aligned pre-invoice documents with VAT, buyer/seller, and line items.
        /// Query by status: ready (new settlements), all, or corrections.
        /// </summary>
        app.MapGet("/api/settlement-documents", async (string? status, WattsOnDbContext db) =>
        {
            var query = db.Settlements
                .Include(a => a.Lines)
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                        .ThenInclude(c => c.SupplierIdentity)
                .AsNoTracking();

            // Filter: "ready" = Beregnet (default), "all" = everything
            if (status == "all")
            {
                // No filter
            }
            else if (status == "corrections")
            {
                query = query.Where(a => a.IsCorrection && a.Status == SettlementStatus.Calculated);
            }
            else // "ready" (default)
            {
                query = query.Where(a => a.Status == SettlementStatus.Calculated);
            }

            var settlements = await query.OrderBy(a => a.DocumentNumber).ToListAsync();

            // Load price VAT info for all referenced prices
            var priceIds = settlements.SelectMany(a => a.Lines).Select(l => l.PriceId).Distinct().ToList();
            var prisVatMap = await db.Prices
                .Where(p => priceIds.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

            const decimal DanishVatRate = 25.0m;

            var documents = settlements.Select(a =>
            {
                var customer = a.Supply.Customer;
                var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
                var isDebit = a.IsCorrection && a.TotalAmount.Amount >= 0;

                var documentType = a.IsCorrection
                    ? (isCredit ? "creditNote" : "debitNote")
                    : "settlement";

                var year = a.CalculatedAt.Year;
                var documentId = $"WO-{year}-{a.DocumentNumber:D5}";
                string? originalDocumentId = null;
                if (a.PreviousSettlementId.HasValue)
                {
                    var original = settlements.FirstOrDefault(s => s.Id == a.PreviousSettlementId);
                    if (original is not null)
                        originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
                }

                var lines = a.Lines.Select((line, idx) =>
                {
                    var prisInfo = prisVatMap.GetValueOrDefault(line.PriceId);
                    var vatExempt = prisInfo?.VatExempt ?? false;
                    var taxPercent = vatExempt ? 0m : DanishVatRate;
                    var taxAmount = vatExempt ? 0m : Math.Round(line.Amount.Amount * taxPercent / 100m, 2);

                    return new
                    {
                        lineNumber = idx + 1,
                        description = line.Description,
                        quantity = line.Quantity.Value,
                        quantityUnit = "KWH",
                        unitPrice = line.UnitPrice,
                        lineAmount = line.Amount.Amount,
                        chargeId = prisInfo?.ChargeId,
                        chargeOwnerGln = prisInfo?.OwnerGln,
                        taxCategory = vatExempt ? "Z" : "S", // S = Standard, Z = Zero-rated
                        taxPercent = taxPercent,
                        taxAmount = taxAmount
                    };
                }).ToList();

                var totalExclVat = a.TotalAmount.Amount;
                var totalVat = lines.Sum(l => l.taxAmount);
                var totalInclVat = totalExclVat + totalVat;

                // Group tax summary by category+rate
                var taxSummary = lines
                    .GroupBy(l => new { l.taxCategory, l.taxPercent })
                    .Select(g => new
                    {
                        taxCategory = g.Key.taxCategory,
                        taxPercent = g.Key.taxPercent,
                        taxableAmount = g.Sum(l => l.lineAmount),
                        taxAmount = g.Sum(l => l.taxAmount)
                    })
                    .ToList();

                return new
                {
                    documentType,
                    documentId,
                    originalDocumentId,
                    settlementId = a.Id,
                    status = a.Status.ToString(),

                    period = new { start = a.SettlementPeriod.Start, end = a.SettlementPeriod.End },

                    seller = new
                    {
                        name = a.Supply.Customer.SupplierIdentity.Name,
                        identifier = a.Supply.Customer.SupplierIdentity.Cvr?.Value,
                        identifierScheme = "DK:CVR",
                        glnNumber = a.Supply.Customer.SupplierIdentity.Gln.Value
                    },
                    buyer = new
                    {
                        name = customer.Name,
                        identifier = customer.IsPrivate ? customer.Cpr?.Value : customer.Cvr?.Value,
                        identifierScheme = customer.IsPrivate ? "DK:CPR" : "DK:CVR",
                        address = customer.Address != null ? new
                        {
                            streetName = customer.Address.StreetName,
                            buildingNumber = customer.Address.BuildingNumber,
                            floor = customer.Address.Floor,
                            suite = customer.Address.Suite,
                            postCode = customer.Address.PostCode,
                            cityName = customer.Address.CityName
                        } : (object?)null
                    },

                    meteringPoint = new
                    {
                        gsrn = a.MeteringPoint.Gsrn.Value,
                        gridArea = a.MeteringPoint.GridArea
                    },

                    lines,
                    taxSummary,

                    totalExclVat,
                    totalVat,
                    totalInclVat,
                    currency = a.TotalAmount.Currency,

                    calculatedAt = a.CalculatedAt,
                    externalInvoiceReference = a.ExternalInvoiceReference,
                    invoicedAt = a.InvoicedAt
                };
            }).ToList();

            return Results.Ok(documents);
        }).WithName("GetSettlementDocuments");

        /// <summary>
        /// Get a single settlement document by ID.
        /// </summary>
        app.MapGet("/api/settlement-documents/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var a = await db.Settlements
                .Include(a => a.Lines)
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                        .ThenInclude(c => c.SupplierIdentity)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

            if (a is null) return Results.NotFound();

            var priceIds = a.Lines.Select(l => l.PriceId).Distinct().ToList();
            var prisVatMap = await db.Prices
                .Where(p => priceIds.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

            const decimal DanishVatRate = 25.0m;

            var customer = a.Supply.Customer;
            var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
            var documentType = a.IsCorrection
                ? (isCredit ? "creditNote" : "debitNote")
                : "settlement";

            var year = a.CalculatedAt.Year;
            var documentId = $"WO-{year}-{a.DocumentNumber:D5}";

            string? originalDocumentId = null;
            if (a.PreviousSettlementId.HasValue)
            {
                var original = await db.Settlements.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == a.PreviousSettlementId);
                if (original is not null)
                    originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
            }

            var lines = a.Lines.Select((line, idx) =>
            {
                var prisInfo = prisVatMap.GetValueOrDefault(line.PriceId);
                var vatExempt = prisInfo?.VatExempt ?? false;
                var taxPercent = vatExempt ? 0m : DanishVatRate;
                var taxAmount = vatExempt ? 0m : Math.Round(line.Amount.Amount * taxPercent / 100m, 2);

                return new
                {
                    lineNumber = idx + 1,
                    description = line.Description,
                    quantity = line.Quantity.Value,
                    quantityUnit = "KWH",
                    unitPrice = line.UnitPrice,
                    lineAmount = line.Amount.Amount,
                    chargeId = prisInfo?.ChargeId,
                    chargeOwnerGln = prisInfo?.OwnerGln,
                    taxCategory = vatExempt ? "Z" : "S",
                    taxPercent,
                    taxAmount
                };
            }).ToList();

            var totalExclVat = a.TotalAmount.Amount;
            var totalVat = lines.Sum(l => l.taxAmount);
            var totalInclVat = totalExclVat + totalVat;

            var taxSummary = lines
                .GroupBy(l => new { l.taxCategory, l.taxPercent })
                .Select(g => new
                {
                    taxCategory = g.Key.taxCategory,
                    taxPercent = g.Key.taxPercent,
                    taxableAmount = g.Sum(l => l.lineAmount),
                    taxAmount = g.Sum(l => l.taxAmount)
                })
                .ToList();

            return Results.Ok(new
            {
                documentType,
                documentId,
                originalDocumentId,
                settlementId = a.Id,
                status = a.Status.ToString(),

                period = new { start = a.SettlementPeriod.Start, end = a.SettlementPeriod.End },

                seller = new
                {
                    name = a.Supply.Customer.SupplierIdentity.Name,
                    identifier = a.Supply.Customer.SupplierIdentity.Cvr?.Value,
                    identifierScheme = "DK:CVR",
                    glnNumber = a.Supply.Customer.SupplierIdentity.Gln.Value
                },
                buyer = new
                {
                    name = customer.Name,
                    identifier = customer.IsPrivate ? customer.Cpr?.Value : customer.Cvr?.Value,
                    identifierScheme = customer.IsPrivate ? "DK:CPR" : "DK:CVR",
                    address = customer.Address != null ? new
                    {
                        streetName = customer.Address.StreetName,
                        buildingNumber = customer.Address.BuildingNumber,
                        floor = customer.Address.Floor,
                        suite = customer.Address.Suite,
                        postCode = customer.Address.PostCode,
                        cityName = customer.Address.CityName
                    } : (object?)null
                },

                meteringPoint = new
                {
                    gsrn = a.MeteringPoint.Gsrn.Value,
                    gridArea = a.MeteringPoint.GridArea
                },

                lines,
                taxSummary,

                totalExclVat,
                totalVat,
                totalInclVat,
                currency = a.TotalAmount.Currency,

                calculatedAt = a.CalculatedAt,
                externalInvoiceReference = a.ExternalInvoiceReference,
                invoicedAt = a.InvoicedAt
            });
        }).WithName("GetSettlementDocument");

        /// <summary>
        /// External system confirms a settlement document has been invoiced.
        /// Accepts the WattsOn document ID or settlement GUID.
        /// </summary>
        app.MapPost("/api/settlement-documents/{id:guid}/confirm", async (Guid id, ConfirmSettlementRequest req, WattsOnDbContext db) =>
        {
            var settlement = await db.Settlements.FindAsync(id);
            if (settlement is null) return Results.NotFound();

            try
            {
                settlement.MarkInvoiced(req.ExternalInvoiceReference);
                await db.SaveChangesAsync();

                var year = settlement.CalculatedAt.Year;
                return Results.Ok(new
                {
                    documentId = $"WO-{year}-{settlement.DocumentNumber:D5}",
                    settlementId = settlement.Id,
                    status = settlement.Status.ToString(),
                    externalInvoiceReference = settlement.ExternalInvoiceReference,
                    invoicedAt = settlement.InvoicedAt
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).WithName("ConfirmSettlementDocument");

        return app;
    }
}
