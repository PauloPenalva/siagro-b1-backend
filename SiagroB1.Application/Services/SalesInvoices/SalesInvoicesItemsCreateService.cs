using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.StorageInvoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesItemsCreateService(
    IUnitOfWork db,
    IItemService itemService,
    SalesInvoicesTaxApplyService taxApply,
    SalesInvoicesFiscalComplementApplier fiscalComplement,
    ILogger<SalesInvoicesItemsCreateService> logger,
    FiscalStockPeriodGuard? periodGuard = null,
    AccountingAssignmentValidator? dimensionValidator = null,
    AccountingAssignmentDefaultsResolver? dimensionDefaults = null)
{
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);
    public async Task ExecuteAsync(SalesInvoiceItem salesInvoiceItem, string userName)
    {
        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5). Fora do try: 400 com a mensagem.
        InvoiceLineChargeRules.Ensure(salesInvoiceItem);
        CustomerOrderRules.Ensure(salesInvoiceItem);

        // Tributação da NF-e STANDALONE: calcula a linha nova antes de gravar. Fora do try para a
        // guarda chegar à tela como 400, e não embrulhada em ApplicationException. No-op com a
        // regra inativa.
        var invoice = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(x => x.Key == salesInvoiceItem.SalesInvoiceKey);

        if (invoice is not null)
        {
            if (SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
                throw new DefaultException(
                    "Na devolução com NF-e, os itens vêm da venda: para devolver outro item, use o Devolver da venda.");

            if (SalesInvoicesTaxApplyService.IsIcmsComplement(invoice))
                throw new DefaultException(SalesInvoiceIcmsComplementLock.LinesChangeMessage);

            SalesInvoiceNfeLock.EnsureLinesChangeable(invoice.NfeStatus, SalesInvoiceNfeLock.IsConfirmedFrozen(
                invoice.InvoiceStatus, invoice.InvoiceType, await taxApply.IsBranchActiveAsync(invoice.BranchCode)));

            // Linha que movimenta o estoque fiscal num documento que já conta: mês fechado recusa.
            if (salesInvoiceItem.MovesFiscalInventory && FiscalStockPeriodGuard.IsBookable(invoice.InvoiceStatus))
                await _periodGuard.EnsureOpenIfDatedAsync(invoice.BranchCode, invoice.InvoiceDate);

            // Complemento fiscal do contrato antes do imposto: a natureza dele é a que o cálculo usa (spec 2026-10-07
            // §4.2). Pode preencher a condição do documento rastreado — o SaveChanges abaixo a grava junto.
            await fiscalComplement.ApplyToLineAsync(invoice, salesInvoiceItem);
            await taxApply.ApplyAsync(invoice, [salesInvoiceItem]);

            // Dimensões gerenciais: o que a linha não trouxe vem do cabeçalho, do parceiro e da natureza; depois valida.
            // Fora do try: a recusa chega à tela como 400. No-op fora do STANDALONE.
            await AccountingAssignmentWiring.ApplyAsync(dimensionDefaults, dimensionValidator, salesInvoiceItem, invoice,
                invoice.CardCode, previous: null, documentDate: invoice.InvoiceDate, itemCode: salesInvoiceItem.ItemCode);
        }

        try
        {
            salesInvoiceItem.ItemName = (await itemService.GetByIdAsync(salesInvoiceItem.ItemCode))?.ItemName;

            await db.Context.SalesInvoicesItems.AddAsync(salesInvoiceItem);
            await StorageInvoiceBillingGuardService.EnsureCanBillAsync(db.Context, salesInvoiceItem.SalesInvoiceKey);
            await db.SaveChangesAsync();

            // Numa devolução o peso do cabeçalho é a soma das linhas. Depois do flush: a soma
            // agrega no SERVIDOR e leria o estado sem esta linha.
            await SalesInvoicesReturnWeightService.RecalculateAsync(
                db.Context, salesInvoiceItem.SalesInvoiceKey);

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError("Error: {message}", e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}