using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Gera o título a RECEBER (do produtor na compra, do cliente na venda) na aprovação de um washout: FIRME (dívida real, liberada
/// para baixa manual) e com origem <see cref="FinancialDocumentOrigin.PurchaseContractWashout"/> ou <see cref="FinancialDocumentOrigin.SalesContractWashout"/>.
///
/// ENQUEUE-ONLY, como <see cref="FinancialDocumentsGenerateService"/>: faz Add e devolve o
/// documento para o chamador gravar a chave no washout. Chame ANTES de abrir transação (Dapper).
/// </summary>
public class FinancialDocumentsGenerateWashoutReceivableService(
    AppDbContext context,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    /// <summary>Washout de compra: título a receber do PRODUTOR, origem PurchaseContractWashout.</summary>
    public Task<FinancialDocument> EnqueueAsync(
        PurchaseContract contract, PurchaseContractWashout washout, string userName) =>
        BuildAsync(FinancialDocumentOrigin.PurchaseContractWashout, washout.Key, washout.Amount, washout.DueDate,
            washout.Reason, contract.Code, contract.CardCode, contract.CardName, contract.BranchCode,
            contract.StandardCurrency, purchaseContractKey: contract.Key, salesContractKey: null, userName);

    /// <summary>
    /// Washout de venda (spec 2026-10-15, §5.2 e §6): título a receber do CLIENTE, origem SalesContractWashout. Não é
    /// firme de documento de saída (IsSalesFirmReceivable falso): baixa em dinheiro, direto, e fica fora da
    /// composição e da trava de encerramento.
    /// </summary>
    public Task<FinancialDocument> EnqueueAsync(
        SalesContract contract, SalesContractWashout washout, string userName) =>
        BuildAsync(FinancialDocumentOrigin.SalesContractWashout, washout.Key, washout.Amount, washout.DueDate,
            washout.Reason, contract.Code, contract.CardCode, contract.CardName, contract.BranchCode,
            contract.StandardCurrency, purchaseContractKey: null, salesContractKey: contract.Key, userName);

    private async Task<FinancialDocument> BuildAsync(
        FinancialDocumentOrigin originType, Guid washoutKey, decimal amount, DateTime? washoutDueDate, string? reason,
        string? contractCode, string cardCode, string? contractCardName, string? branchCode, CurrencyType? currency,
        Guid? purchaseContractKey, Guid? salesContractKey, string userName)
    {
        if (amount <= 0)
            throw new ApplicationException("Washout sem valor não gera título a receber.");

        var dueDate = washoutDueDate
                      ?? throw new ApplicationException("Informe o vencimento do título a receber do washout.");

        var existing = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == originType &&
            x.OriginKey == washoutKey &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (existing is not null) return existing;

        var partner = await businessPartnerService.GetByIdAsync(cardCode);
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = await docNumberSequence.GetDocNumber(docNumberKey),
            DocNumberKey = docNumberKey,
            BranchCode = branchCode,
            Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = partner?.CardName ?? contractCardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = currency ?? CurrencyType.Brl,
            NetAmount = amount,
            SettledAmount = 0m,
            OriginType = originType,
            OriginKey = washoutKey,
            OriginDocNumber = contractCode,
            PurchaseContractKey = purchaseContractKey,
            SalesContractKey = salesContractKey,
            Comments = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        context.FinancialDocuments.Add(document);
        return document;
    }
}
