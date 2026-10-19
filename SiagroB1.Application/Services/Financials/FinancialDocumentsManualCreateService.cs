using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Inclusão manual de título nas telas de Contas a Pagar/Receber (natureza Firme, origem Manual), sem contrato nem
/// documento por trás. Número pela série escolhida: Automático (contador) ou Manual (código digitado).
/// </summary>
public class FinancialDocumentsManualCreateService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<FinancialDocument> ExecuteAsync(FinancialDocument input, string userName)
    {
        var documentDate = input.DocumentDate == default ? DateTime.Today : input.DocumentDate;
        FinancialDocumentsManualRules.Validate(input.CardCode, input.BranchCode, documentDate, input.DueDate, input.NetAmount);

        if (input.Direction is not (FinancialDirection.Payable or FinancialDirection.Receivable))
            throw new ApplicationException("Direção do título inválida.");

        var cardCode = input.CardCode.Trim();
        var partner = await businessPartnerService.GetByIdAsync(cardCode)
                      ?? throw new ApplicationException($"Parceiro de negócio {cardCode} não encontrado.");

        // Número buscado ANTES da gravação: Dapper em conexão separada, segurando UPDLOCK.
        var assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
            TransactionCode.FinancialDocument, input.DocNumberKey, input.Code, 50,
            c => db.Context.FinancialDocuments.AnyAsync(x => x.Code == c)));

        var now = DateTime.Now;
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = assignment.Code,
            DocNumberKey = assignment.DocNumberKey,
            BranchCode = input.BranchCode!.Trim(),
            Direction = input.Direction,
            Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = partner.CardName,
            DocumentDate = documentDate,
            DueDate = input.DueDate,
            Currency = input.Currency,
            NetAmount = decimal.Round(input.NetAmount, 2, MidpointRounding.ToEven),
            OriginType = FinancialDocumentOrigin.Manual,
            TaxDocumentNumber = FinancialDocumentsManualRules.Clean(input.TaxDocumentNumber),
            PaymentTermsText = FinancialDocumentsManualRules.Clean(input.PaymentTermsText),
            Comments = FinancialDocumentsManualRules.Clean(input.Comments),
            CreatedAt = now,
            CreatedBy = userName,
            UpdatedAt = now,
            UpdatedBy = userName
        };

        db.Context.FinancialDocuments.Add(document);
        await db.Context.SaveChangesAsync();

        return document;
    }
}
