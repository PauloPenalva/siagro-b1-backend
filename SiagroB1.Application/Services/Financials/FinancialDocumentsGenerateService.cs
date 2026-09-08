using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Gera o documento financeiro PROVISÓRIO de UMA fixação de preço confirmada.
///
/// ENQUEUE-ONLY: faz Add e nunca chama SaveChangesAsync. É o que permite engancharem-no nos
/// serviços de aprovação de contrato, que injetam AppDbContext direto e salvam uma vez só —
/// o documento e a mudança de status caem no MESMO SaveChanges, atômicos sem transação
/// explícita. Mesmo idioma de ContractNotificationOutboxService.Register.
///
/// Um caminho de código para os dois tipos de contrato: o de preço fixo já nasce com uma
/// fixação Confirmed (PurchaseContractsCreateService.CreatePriceFixation), então "iterar as
/// fixações confirmadas" produz 1 documento no FIX e 0 no PAF, sem nenhum if de tipo.
///
/// ⚠️ Chame ANTES de abrir transação: DocNumberSequenceService roda por Dapper numa conexão
/// separada, que não participa da transação do EF, e segura UPDLOCK em DOC_NUMBERS.
///
/// FASE 2: quando existir documento FIRME, o valor terá de descontar os firmes já emitidos
/// contra esta origem, senão a reabertura de contrato conta duas vezes. Hoje essa soma é
/// sempre zero e a consulta seria código morto.
/// </summary>
public class FinancialDocumentsGenerateService(
    AppDbContext context,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    public Task EnqueueForPurchaseFixationAsync(
        PurchaseContract contract, PurchaseContractPriceFixation fixation, string userName) =>
        EnqueueAsync(
            direction: FinancialDirection.Payable,
            originType: FinancialDocumentOrigin.PurchaseContractPriceFixation,
            originKey: fixation.Key,
            contractCode: contract.Code,
            cardCode: contract.CardCode,
            branchCode: contract.BranchCode,
            currency: contract.StandardCurrency ?? CurrencyType.Brl,
            contractType: contract.Type,
            volume: fixation.FixationVolume,
            price: fixation.FixationPrice,
            fixationDueDate: fixation.FinancialDueDate,
            contractCashFlowDate: contract.StandardCashFlowDate,
            paymentTermsText: fixation.PaymentDetails ?? contract.PaymentTerms,
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            userName: userName);

    public Task EnqueueForSalesFixationAsync(
        SalesContract contract, SalesContractPriceFixation fixation, string userName) =>
        EnqueueAsync(
            direction: FinancialDirection.Receivable,
            originType: FinancialDocumentOrigin.SalesContractPriceFixation,
            originKey: fixation.Key,
            contractCode: contract.Code,
            cardCode: contract.CardCode,
            branchCode: contract.BranchCode,
            currency: contract.StandardCurrency ?? CurrencyType.Brl,
            contractType: contract.Type,
            volume: fixation.FixationVolume,
            price: fixation.FixationPrice,
            fixationDueDate: fixation.FinancialDueDate,
            contractCashFlowDate: contract.StandardCashFlowDate,
            paymentTermsText: fixation.PaymentDetails ?? contract.PaymentTerms,
            purchaseContractKey: null,
            salesContractKey: contract.Key,
            userName: userName);

    private async Task EnqueueAsync(
        FinancialDirection direction,
        FinancialDocumentOrigin originType,
        Guid originKey,
        string? contractCode,
        string cardCode,
        string? branchCode,
        CurrencyType currency,
        ContractType contractType,
        decimal volume,
        decimal price,
        DateTime? fixationDueDate,
        DateTime? contractCashFlowDate,
        string? paymentTermsText,
        Guid? purchaseContractKey,
        Guid? salesContractKey,
        string userName)
    {
        var dueDate = fixationDueDate ?? contractCashFlowDate
            ?? throw new ApplicationException(
                contractType == ContractType.ToBeDetermined
                    ? "Informe o vencimento financeiro da fixação de preço para gerar o título."
                    : "Informe a previsão de pagamento do contrato para gerar o título.");

        // Primeira camada de idempotência: mensagem amigável. A segunda é o índice único
        // filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin, que sobrevive a um caminho novo
        // que esqueça esta consulta.
        var alreadyGenerated = await context.FinancialDocuments.AnyAsync(x =>
            x.OriginType == originType &&
            x.OriginKey == originKey &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (alreadyGenerated) return;

        var partner = await businessPartnerService.GetByIdAsync(cardCode);

        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);

        context.FinancialDocuments.Add(new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = await docNumberSequence.GetDocNumber(docNumberKey),
            DocNumberKey = docNumberKey,
            BranchCode = branchCode,
            Direction = direction,
            Nature = FinancialDocumentNature.Provisional,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = partner?.CardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = currency,
            NetAmount = decimal.Round(volume * price, 2, MidpointRounding.ToEven),
            SettledAmount = 0m,
            OriginType = originType,
            OriginKey = originKey,
            OriginDocNumber = contractCode,
            PurchaseContractKey = purchaseContractKey,
            SalesContractKey = salesContractKey,
            PaymentTermsText = paymentTermsText,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        });
    }
}
