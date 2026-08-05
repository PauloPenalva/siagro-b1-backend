using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesCreateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    DocNumberSequenceService numberSequenceService,
    SalesInvoicesUsageGuardService usageGuard,
    SalesInvoicesCfopResolveService cfopResolve,
    ILogger<SalesInvoicesCreateService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (salesInvoice.Items.Count == 0)
            throw new ApplicationException("Items can not be empty.");

        // Natureza de operação e CFOP são resolvidos ANTES de qualquer gravação: os dois
        // rejeitam com mensagem de negócio, e não faz sentido numerar um documento que não
        // vai nascer. Vale para o documento avulso e para o faturamento de romaneio — o
        // laço de romaneios abaixo já é no-op quando SalesTransactions está vazio, que é
        // exatamente o caso avulso.
        //
        // A natureza é de LINHA: cada item resolve o próprio CFOP, e um documento pode
        // misturar naturezas (venda numa linha, complemento em outra).
        var lineUsages = await usageGuard.ValidateAsync(salesInvoice);
        var cfopByItem = new Dictionary<SalesInvoiceItem, string>();

        foreach (var (item, usage) in lineUsages)
        {
            cfopByItem[item] = await cfopResolve.ResolveAsync(
                usage.Code, salesInvoice.BranchCode, salesInvoice.CardCode);

            // Nome desnormalizado vem do servidor, não da tela — mesmo tratamento de ItemName.
            item.UsageName = usage.Name;
        }

        salesInvoice.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.SalesInvoice);

        try
        {
            salesInvoice.CreatedAt = DateTime.Now;
            salesInvoice.CreatedBy = userName;
            salesInvoice.InvoiceNumber = await numberSequenceService.GetDocNumber((Guid) salesInvoice.DocNumberKey);
            salesInvoice.InvoiceStatus = InvoiceStatus.Pending;
            salesInvoice.CardName = (await businessPartnerService.GetByIdAsync(salesInvoice.CardCode))?.CardName;
            salesInvoice.TruckingCompanyName =
                salesInvoice.TruckingCompanyCode != null
                    ? (await businessPartnerService.GetByIdAsync(salesInvoice.TruckingCompanyCode))?.CardName
                    : string.Empty;
            salesInvoice.DeliveryCardName =
                salesInvoice.DeliveryCardCode != null
                    ? (await businessPartnerService.GetByIdAsync(salesInvoice.DeliveryCardCode))?.CardName
                    : string.Empty;

            foreach (var item in salesInvoice.Items)
            {
                item.ItemName = (await itemService.GetByIdAsync(item.ItemCode))?.ItemName;

                // CFOP congelado como histórico da linha: mudar o cadastro da natureza
                // depois não pode mudar o documento já emitido.
                item.Cfop = cfopByItem[item];
            }

            var salesTransactions = new List<Guid>();

            foreach (var salesTransaction in salesInvoice.SalesTransactions)
            {
                salesTransactions.Add(salesTransaction.Key);
            }

            salesInvoice.SalesTransactions.Clear();

            await db.Context.SalesInvoices.AddAsync(salesInvoice);

            // Liberação de entrega de venda selecionada no faturamento (um contrato/liberação
            // por invoice — mesmo produto/veículo). Grava-se a chave nos romaneios para que a
            // liberação consuma o saldo; o recálculo do ShippedQuantity é disparado pelo
            // orquestrador (ShipmentBillingCreateSalesInvoiceService) após o SaveChanges.
            var salesShipmentReleaseKey = salesInvoice.Items
                .FirstOrDefault(i => i.SalesShipmentReleaseKey != null)?.SalesShipmentReleaseKey;

            foreach (var transactionKey in salesTransactions)
            {
                var existingTransaction = await db.Context.StorageTransactions
                    .FirstOrDefaultAsync(x => x.Key == transactionKey) ??
                                          throw new ApplicationException($"Transaction {transactionKey} not found.");

                // Rede de segurança: nunca re-apontar um romaneio já vinculado a outro
                // documento de saída — era assim que a duplicidade deixava a invoice
                // anterior órfã e o saldo do contrato descontado duas vezes. A mensagem
                // amigável vem do ShipmentBillingTransactionGuardService, antes daqui.
                if (existingTransaction.SalesInvoiceKey != null)
                    throw new ApplicationException(
                        $"Romaneio {existingTransaction.Code} já está vinculado ao documento " +
                        $"de saída {existingTransaction.InvoiceNumber}.");

                existingTransaction.InvoiceNumber = salesInvoice.InvoiceNumber;
                existingTransaction.InvoiceQty = existingTransaction.GrossWeight;
                existingTransaction.SalesInvoiceKey = salesInvoice.Key;
                existingTransaction.SalesShipmentReleaseKey = salesShipmentReleaseKey;
                existingTransaction.TransactionStatus = StorageTransactionsStatus.Invoiced;
            }
            
            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError("Error: {message}", e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}