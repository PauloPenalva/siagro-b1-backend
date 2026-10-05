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
    SalesInvoicesTaxApplyService taxApply,
    ILogger<SalesInvoicesCreateService> logger)
{
    public async Task ExecuteAsync(
        SalesInvoice salesInvoice, string userName, CommitMode commitMode = CommitMode.Auto, bool nfeReturn = false)
    {
        if (salesInvoice.Items.Count == 0)
            throw new ApplicationException("Items can not be empty.");

        // Os campos da NF-e só a emissão escreve: um corpo com "Autorizada" passaria pela guarda
        // da confirmação direta.
        SalesInvoiceNfeLock.ResetIssuanceFields(salesInvoice);

        // Só o "Devolver" (SalesInvoicesNfeReturnCreateService) marca a devolução com NF-e própria;
        // o corpo da API nunca — senão um POST tiraria a nota da confirmação direta.
        salesInvoice.IsNfeReturn = nfeReturn && salesInvoice.InvoiceType == SalesInvoiceType.Return;

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

        // Documento nascido de romaneio — por CARGA ou pelo caminho legado. A coleção
        // SalesTransactions é vazia no documento de carga (ele chega ao romaneio pela carga),
        // então perguntá-la sozinha classificaria a carga como avulsa e LIGARIA a exigência de
        // CFOP: numa base sem natureza padrão o faturamento passaria a RECUSAR, que é
        // exatamente o que o <remarks> deste serviço diz que não pode acontecer.
        var fromShipmentBilling = SalesInvoiceOriginResolver.ConsumesShipments(salesInvoice);

        // Com a tributação da NF-e STANDALONE ativa, a cadeia fiscal volta a ser ESTRITA também
        // no romaneio: a tolerância abaixo existe para bases sem cadastro fiscal (SAPB1 recém-
        // implantada), e uma filial que emite NF-e não pode faturar com CFOP em branco.
        var taxActive = await taxApply.IsActiveForAsync(salesInvoice);
        var cfopByItem = new Dictionary<SalesInvoiceItem, string>();

        foreach (var (item, usage) in lineUsages)
        {
            // A devolução própria usa natureza de ENTRADA: o CFOP (1202/2202) sai do cálculo, e a
            // resolução de saída abaixo recusaria a natureza.
            if (!salesInvoice.IsNfeReturn)
            {
                var cfop = await ResolveCfopAsync(salesInvoice, usage, fromShipmentBilling && !taxActive);

                if (cfop != null)
                {
                    cfopByItem[item] = cfop;
                }
            }

            // Nome desnormalizado vem do servidor, não da tela — mesmo tratamento de ItemName.
            item.UsageName = usage.Name;
        }

        // Tributos pela natureza, ANTES de numerar: as guardas recusam com mensagem de negócio e
        // não faz sentido consumir número de um documento que não vai nascer. Fora do try de
        // propósito — lá dentro a DefaultException viraria ApplicationException e perderia o 400.
        if (taxActive)
        {
            // Todo documento nasce Pendente (o try abaixo força o mesmo). Forçar ANTES do
            // cálculo: o status vem do corpo, e o cálculo só age em documento Pendente — um
            // "Confirmado" escolhido na tela faria a linha nascer sem imposto e sem CFOP.
            salesInvoice.InvoiceStatus = InvoiceStatus.Pending;
            await taxApply.ApplyAsync(salesInvoice, salesInvoice.Items);
        }

        salesInvoice.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.SalesInvoice);

        try
        {
            salesInvoice.CreatedAt = DateTime.Now;
            salesInvoice.CreatedBy = userName;
            salesInvoice.InvoiceNumber = await numberSequenceService.GetDocNumber((Guid) salesInvoice.DocNumberKey);
            salesInvoice.InvoiceStatus = InvoiceStatus.Pending;
            var customer = await businessPartnerService.GetByIdAsync(salesInvoice.CardCode);
            salesInvoice.CardName = customer?.CardName;

            // Condição de pagamento padrão do cliente quando o documento chega sem ela — inclusive
            // no faturamento de romaneio. Em SAPB1 o parceiro não tem o campo: segue nulo, como hoje.
            // A devolução própria não tem pagamento (tPag 90): a condição do cliente só confundiria.
            if (!salesInvoice.IsNfeReturn)
                salesInvoice.PaymentConditionCode ??= customer?.PaymentConditionCode;
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
                // fiscal ainda não está completo e o documento veio de romaneio. Com a
                // tributação ativa ele já veio do cálculo (mesma regra, mesmo valor).
                if (!taxActive)
                {
                    item.Cfop = cfopByItem.GetValueOrDefault(item);
                }
            }

            // Numa DEVOLUÇÃO o peso do cabeçalho é a soma das linhas — inclusive na criada à
            // mão pela tela, que não passa pelo SalesInvoiceReturnFactory. Sem isto ela
            // nasceria com o peso que a tela mandou (ou zero) e a confirmação a recusaria, com
            // o campo já travado e sem saída. Ver SalesInvoicesReturnWeightService.
            SalesInvoicesReturnWeightService.Apply(salesInvoice);

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