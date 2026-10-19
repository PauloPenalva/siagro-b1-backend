using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageInvoices;

/// <summary>Tela da fatura (spec 2026-10-19 parte 3): documentos de saída vinculados, títulos a receber deles e saldo a faturar.</summary>
public class StorageInvoicesBillingGetService(IUnitOfWork db)
{
    public async Task<StorageInvoiceBillingDto> ExecuteAsync(Guid key)
    {
        var context = db.Context;
        var total = await context.StorageInvoices.AsNoTracking().Where(i => i.Key == key)
                        .Select(i => (decimal?)i.TotalAmount).FirstOrDefaultAsync()
                    ?? throw new NotFoundException("Fatura de armazenagem não encontrada.");

        var documents = await context.SalesInvoices.AsNoTracking().Include(i => i.Items)
            .Where(i => i.StorageInvoiceKey == key)
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber)
            .ToListAsync();
        var keys = documents.Select(d => d.Key).ToList();
        var numbers = documents.ToDictionary(d => d.Key, d => d.InvoiceNumber);

        var titles = await context.FinancialDocuments.AsNoTracking()
            .Where(d => d.SalesInvoiceKey != null && keys.Contains(d.SalesInvoiceKey.Value) &&
                        d.Direction == FinancialDirection.Receivable)
            .OrderBy(d => d.DueDate).ThenBy(d => d.Code)
            .ToListAsync();

        return new StorageInvoiceBillingDto
        {
            TotalAmount = total,
            BilledAmount = documents.Where(d => d.InvoiceStatus is InvoiceStatus.Confirmed or InvoiceStatus.Returned)
                .Sum(d => d.Items.Sum(l => l.GrandTotal)),
            Balance = await StorageInvoiceBillingGuardService.GetBalanceAsync(context, key),
            Documents = documents.Select(d => new StorageInvoiceBillingDocumentDto
            {
                Key = d.Key, InvoiceNumber = d.InvoiceNumber, TaxDocumentKind = d.TaxDocumentKind,
                TaxDocumentNumber = d.TaxDocumentNumber, TaxDocumentSeries = d.TaxDocumentSeries,
                InvoiceDate = d.InvoiceDate, Total = d.Items.Sum(l => l.GrandTotal), InvoiceStatus = d.InvoiceStatus,
            }).ToList(),
            Receivables = titles.Select(t => new StorageInvoiceBillingReceivableDto
            {
                Key = t.Key, Code = t.Code, InvoiceNumber = numbers.GetValueOrDefault(t.SalesInvoiceKey!.Value),
                InstallmentNumber = t.InstallmentNumber, DueDate = t.DueDate, NetAmount = t.NetAmount,
                OpenAmount = t.Status == FinancialDocumentStatus.Canceled ? 0m : t.OpenAmount, Status = t.Status,
            }).ToList(),
        };
    }
}
