using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Regras do CT-e de frete tomado no Documento de Entrada (spec 2026-10-17): é sempre documento de terceiro, do tipo
/// Normal, com uma única linha (o serviço de transporte), UFs da prestação e o tomador sendo a filial do documento.
/// </summary>
public static class PurchaseInvoiceCteRules
{
    public static bool IsCte(PurchaseInvoice invoice) => invoice.TaxDocumentKind == TaxDocumentKind.Cte;

    public const string OwnIssuerMessage = "CT-e é sempre documento de terceiro.";

    /// <summary>Recusa na ordem da spec; normaliza as UFs (maiúsculas) e o tomador (só dígitos).</summary>
    public static async Task EnsureAsync(AppDbContext ctx, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        if (invoice.IssuerType == DocumentIssuerType.Own)
            throw new DefaultException(OwnIssuerMessage);

        if (invoice.InvoiceType != PurchaseInvoiceType.Normal)
            throw new DefaultException("CT-e só pode ser do tipo Normal.");

        var start = (invoice.FreightStartState ?? string.Empty).Trim().ToUpperInvariant();
        var end = (invoice.FreightEndState ?? string.Empty).Trim().ToUpperInvariant();
        if (start.Length != 2 || end.Length != 2)
            throw new DefaultException("Informe a UF de início e a UF de fim da prestação do CT-e.");

        invoice.FreightStartState = start;
        invoice.FreightEndState = end;

        var payer = Digits(invoice.FreightPayerTaxId);
        if (payer.Length == 0)
            throw new DefaultException("Informe o CNPJ do tomador do CT-e.");

        invoice.FreightPayerTaxId = payer;

        var branchTaxId = Digits(await ctx.Branchs.AsNoTracking()
            .Where(b => b.Code == invoice.BranchCode).Select(b => b.TaxId).FirstOrDefaultAsync());
        if (payer != branchTaxId)
            throw new DefaultException($"O tomador do CT-e (CNPJ {payer}) não é a filial do documento.");

        if (lines.Count != 1)
            throw new DefaultException("O CT-e tem uma única linha: o serviço de transporte.");
    }

    private static string Digits(string? value) => string.Concat((value ?? string.Empty).Where(char.IsAsciiDigit));
}
