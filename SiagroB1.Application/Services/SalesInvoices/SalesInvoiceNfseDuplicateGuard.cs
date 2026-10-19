using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// NFS digitada (spec 2026-10-19 parte 2): a nota já foi emitida no portal da prefeitura, então o Confirmar exige número,
/// série e data, e recusa a mesma NFS (PN + série + número) num segundo documento não cancelado.
/// </summary>
internal static class SalesInvoiceNfseDuplicateGuard
{
    public static async Task EnsureConfirmableAsync(AppDbContext context, SalesInvoice invoice)
    {
        if (invoice.TaxDocumentKind != TaxDocumentKind.Nfse) return;

        if (string.IsNullOrWhiteSpace(invoice.TaxDocumentNumber) || string.IsNullOrWhiteSpace(invoice.TaxDocumentSeries) ||
            invoice.InvoiceDate is null)
            throw new DefaultException("Informe número, série e data de emissão da NFS.");

        await EnsureNotDuplicateAsync(context, invoice);
    }

    public static async Task EnsureNotDuplicateAsync(AppDbContext context, SalesInvoice invoice)
    {
        var number = invoice.TaxDocumentNumber!.Trim();
        var series = invoice.TaxDocumentSeries!.Trim();
        var other = await context.SalesInvoices.AsNoTracking()
            .Where(i => i.Key != invoice.Key && i.TaxDocumentKind == TaxDocumentKind.Nfse &&
                        i.CardCode == invoice.CardCode && i.InvoiceStatus != InvoiceStatus.Cancelled &&
                        i.TaxDocumentNumber != null && i.TaxDocumentSeries != null &&
                        i.TaxDocumentNumber.Trim() == number && i.TaxDocumentSeries.Trim() == series)
            .Select(i => i.InvoiceNumber)
            .FirstOrDefaultAsync();
        if (other is not null)
            throw new DefaultException($"Já existe a NFS {series}/{number} para este parceiro (documento {other}).");
    }
}
