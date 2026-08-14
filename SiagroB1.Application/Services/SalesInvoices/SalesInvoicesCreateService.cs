using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
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
        //
        // A lista vem PARCIAL quando o romaneio fatura em base sem natureza padrão: as linhas
        // ausentes ficam sem natureza e sem CFOP, de propósito.
        var lineUsages = await usageGuard.ValidateAsync(salesInvoice);

        // Documento nascido de romaneio. O caminho avulso tem a coleção vazia.
        var fromShipmentBilling = salesInvoice.SalesTransactions.Count > 0;
        var cfopByItem = new Dictionary<SalesInvoiceItem, string>();

        foreach (var (item, usage) in lineUsages)
        {
            var cfop = await ResolveCfopAsync(salesInvoice, usage, fromShipmentBilling);

            if (cfop != null)
            {
                cfopByItem[item] = cfop;
            }

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
                // depois não pode mudar o documento já emitido. Ausente quando o cadastro
                // fiscal ainda não está completo e o documento veio de romaneio.
                item.Cfop = cfopByItem.GetValueOrDefault(item);
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

    /// <summary>
    /// CFOP da linha. No documento AVULSO qualquer lacuna do cadastro é erro de negócio e sobe
    /// para a tela — é lá que a natureza produz efeito no contrato, e o usuário escolheu a
    /// natureza sabendo disso.
    ///
    /// No documento nascido de ROMANEIO o cadastro incompleto não pode barrar o registro: o
    /// caminhão já saiu e já pesou. As lacunas reais numa base recém-implantada são a UF da
    /// filial e a UF do parceiro (colunas novas, sem backfill possível) e o CFOP não preenchido
    /// na natureza. Aqui elas viram aviso no log e a linha nasce sem CFOP — metadado fiscal em
    /// branco, saldo do contrato intacto.
    /// </summary>
    private async Task<string?> ResolveCfopAsync(
        SalesInvoice salesInvoice, UsageModel usage, bool fromShipmentBilling)
    {
        try
        {
            return await cfopResolve.ResolveAsync(
                usage.Code, salesInvoice.BranchCode, salesInvoice.CardCode);
        }
        catch (DefaultException e) when (fromShipmentBilling)
        {
            logger.LogWarning(
                "Faturamento de romaneio sem CFOP na natureza {UsageCode}: {Message}",
                usage.Code, e.Message);

            return null;
        }
    }
}