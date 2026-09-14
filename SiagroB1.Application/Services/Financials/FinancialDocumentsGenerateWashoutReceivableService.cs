using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Gera o título a RECEBER do produtor na aprovação de um washout: FIRME (dívida real, liberada
/// para baixa manual) e com origem <see cref="FinancialDocumentOrigin.PurchaseContractWashout"/>.
///
/// ENQUEUE-ONLY, como <see cref="FinancialDocumentsGenerateService"/>: faz Add e devolve o
/// documento para o chamador gravar a chave no washout. Chame ANTES de abrir transação (Dapper).
/// </summary>
public class FinancialDocumentsGenerateWashoutReceivableService(
    AppDbContext context,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<FinancialDocument> EnqueueAsync(
        PurchaseContract contract, PurchaseContractWashout washout, string userName)
    {
        if (washout.Amount <= 0)
            throw new ApplicationException("Washout sem valor não gera título a receber.");

        var dueDate = washout.DueDate
                      ?? throw new ApplicationException("Informe o vencimento do título a receber do washout.");

        var existing = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.PurchaseContractWashout &&
            x.OriginKey == washout.Key &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (existing is not null) return existing;

        var partner = await businessPartnerService.GetByIdAsync(contract.CardCode);
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = await docNumberSequence.GetDocNumber(docNumberKey),
            DocNumberKey = docNumberKey,
            BranchCode = contract.BranchCode,
            Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            CardCode = contract.CardCode,
            CardName = partner?.CardName ?? contract.CardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = contract.StandardCurrency ?? CurrencyType.Brl,
            NetAmount = washout.Amount,
            SettledAmount = 0m,
            OriginType = FinancialDocumentOrigin.PurchaseContractWashout,
            OriginKey = washout.Key,
            OriginDocNumber = contract.Code,
            PurchaseContractKey = contract.Key,
            Comments = washout.Reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        context.FinancialDocuments.Add(document);
        return document;
    }
}
