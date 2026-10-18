using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Travas do complemento de ICMS de saída (spec 2026-10-18 S6): ele é a imagem da nota original — cliente, filial e tipo
/// não mudam, e as linhas são as escolhidas na criação. Como a trava da devolução própria, sobrescreve com o gravado em
/// vez de recusar: o PATCH da tela reenvia a entidade inteira. A linha tem caminho próprio em
/// <see cref="SalesInvoicesItemsUpdateService"/> (só Base/ICMS mudam, e só em Pendente).
/// </summary>
public static class SalesInvoiceIcmsComplementLock
{
    /// <summary>V7.</summary>
    public const string LinesChangeMessage = "O complemento de ICMS não aceita inclusão nem remoção de itens.";

    /// <summary>POST cru de documento com tipo complemento.</summary>
    public const string CreateMessage = "Complemento de ICMS só nasce do Complementar ICMS da venda.";

    private static readonly string[] HeaderFields =
    [
        nameof(SalesInvoice.CardCode), nameof(SalesInvoice.CardName), nameof(SalesInvoice.BranchCode),
        nameof(SalesInvoice.InvoiceType), nameof(SalesInvoice.SalesInvoiceOriginKey),
        // Trocar para "Outro" tiraria o complemento da emissão da NF-e — e ele não tem outro caminho.
        nameof(SalesInvoice.TaxDocumentKind),
    ];

    /// <summary>
    /// Chamado DEPOIS do SetValues. Gravado como complemento: o cabeçalho travado volta ao gravado. Os demais documentos
    /// não viram complemento pelo PATCH/PUT — ele só nasce da ação na nota original.
    /// </summary>
    public static void RestoreHeader(EntityEntry<SalesInvoice> entry)
    {
        var storedType = (SalesInvoiceType)entry.OriginalValues[nameof(SalesInvoice.InvoiceType)]!;

        if (storedType == SalesInvoiceType.IcmsComplement)
        {
            foreach (var field in HeaderFields)
                entry.Property(field).CurrentValue = entry.OriginalValues[field];
            return;
        }

        if (entry.Entity.InvoiceType == SalesInvoiceType.IcmsComplement)
            entry.Property(nameof(SalesInvoice.InvoiceType)).CurrentValue = storedType;
    }
}
