using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Travas da devolução própria (spec §7): ela é a imagem da venda — cliente, filial, produto, preço
/// e natureza vêm de lá. Como as travas do sub-projeto 1, sobrescreve com o gravado em vez de
/// recusar: o PATCH da tela reenvia a entidade inteira.
/// </summary>
public static class SalesInvoiceNfeReturnLock
{
    private static readonly string[] HeaderFields =
    [
        nameof(SalesInvoice.CardCode), nameof(SalesInvoice.CardName), nameof(SalesInvoice.BranchCode),
        nameof(SalesInvoice.InvoiceType), nameof(SalesInvoice.SalesInvoiceOriginKey),
    ];

    /// <summary>
    /// Restaurados na linha da devolução. Quantidade e os quatro valores da linha (frete, seguro, desconto, outras
    /// despesas) ficam de fora de propósito: nascem da venda na proporção do que volta e são editáveis enquanto a
    /// devolução está Pendente (spec 2026-10-05 D4).
    /// </summary>
    private static readonly string[] LineFields =
    [
        nameof(SalesInvoiceItem.ItemCode), nameof(SalesInvoiceItem.UnitOfMeasureCode), nameof(SalesInvoiceItem.UnitPrice),
        nameof(SalesInvoiceItem.UsageCode), nameof(SalesInvoiceItem.SalesInvoiceItemOriginKey),
        nameof(SalesInvoiceItem.SalesContractKey),
    ];

    /// <summary>Chamado DEPOIS do SetValues; só age se o gravado é uma devolução própria.</summary>
    public static void RestoreHeader(EntityEntry<SalesInvoice> entry)
    {
        if (entry.OriginalValues[nameof(SalesInvoice.IsNfeReturn)] is not true)
            return;

        foreach (var field in HeaderFields)
            entry.Property(field).CurrentValue = entry.OriginalValues[field];
    }

    /// <summary>Chamado DEPOIS do SetValues da linha de uma devolução própria: só a quantidade e os quatro valores da linha mudam.</summary>
    public static void RestoreLine(EntityEntry<SalesInvoiceItem> entry)
    {
        foreach (var field in LineFields)
            entry.Property(field).CurrentValue = entry.OriginalValues[field];
    }
}
