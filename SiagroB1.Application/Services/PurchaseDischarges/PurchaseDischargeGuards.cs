using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseDischarges;

/// <summary>
/// Troca nota: nota, item ou Expedição com romaneio de quebra/sobra de descarga ATIVO não pode ser
/// excluído/cancelado/estornado — as FKs são NoAction (o banco daria 547 → 500 mudo) e, mais
/// importante, o acerto do fornecedor ficaria órfão. Quem desfaz é reabrir a conferência.
/// </summary>
public static class PurchaseDischargeGuards
{
    public const string InvoiceMessage =
        "Este documento de saída tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";
    public const string ItemMessage =
        "Este item tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";
    public const string ShippingMessage =
        "Esta Expedição tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";

    private static IQueryable<Domain.Entities.StorageTransaction> Active(AppDbContext ctx) =>
        ctx.StorageTransactions.Where(x =>
            (x.TransactionType == StorageTransactionType.PurchaseDischargeLoss ||
             x.TransactionType == StorageTransactionType.PurchaseDischargeGain) &&
            x.TransactionStatus != StorageTransactionsStatus.Cancelled);

    public static Task<bool> HasActiveForItemAsync(AppDbContext ctx, Guid salesInvoiceItemKey) =>
        Active(ctx).AnyAsync(x => x.DischargeSalesInvoiceItemKey == salesInvoiceItemKey);

    public static Task<bool> HasActiveForInvoiceAsync(AppDbContext ctx, Guid salesInvoiceKey) =>
        Active(ctx).AnyAsync(x => ctx.SalesInvoicesItems.Any(i =>
            i.Key == x.DischargeSalesInvoiceItemKey && i.SalesInvoiceKey == salesInvoiceKey));

    public static Task<bool> HasActiveForShippingAsync(AppDbContext ctx, Guid shippingTransactionKey) =>
        Active(ctx).AnyAsync(x => x.DischargeShippingTransactionKey == shippingTransactionKey);
}
