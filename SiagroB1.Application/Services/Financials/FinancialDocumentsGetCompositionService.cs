using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Leitura da composição de um título (spec 2026-10-13, títulos firmes) — a function basta; não existe entity set de
/// composição. Projeção em memória porque OpenAmount é [NotMapped].
/// </summary>
public class FinancialDocumentsGetCompositionService(IUnitOfWork db)
{
    public async Task<FinancialDocumentCompositionDto> ExecuteAsync(Guid key)
    {
        var context = db.Context;
        var document = await context.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var rows = await context.FinancialDocumentCompositions.AsNoTracking()
            .Where(c => c.ProvisionalDocumentKey == key || c.ComposedDocumentKey == key)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var documentKeys = rows.SelectMany(r => new[] { r.ProvisionalDocumentKey, r.ComposedDocumentKey }).Distinct().ToList();
        var documents = await context.FinancialDocuments.AsNoTracking()
            .Where(d => documentKeys.Contains(d.Key))
            .ToDictionaryAsync(d => d.Key);

        var lines = rows.Select(r =>
        {
            var composed = documents[r.ComposedDocumentKey];
            return new FinancialDocumentCompositionLineDto
            {
                CompositionKey = r.Key,
                ProvisionalDocumentKey = r.ProvisionalDocumentKey,
                ProvisionalCode = documents[r.ProvisionalDocumentKey].Code,
                DocumentKey = composed.Key,
                DocumentCode = composed.Code,
                Nature = composed.Nature.ToString(),
                StorageTransactionCode = composed.StorageTransactionCode,
                TaxDocumentNumber = composed.TaxDocumentNumber,
                CardCode = composed.CardCode,
                CardName = composed.CardName,
                Amount = r.Amount,
                CreatedAt = r.CreatedAt,
                CreatedBy = r.CreatedBy,
            };
        }).ToList();

        List<FinancialDocumentCompositionCandidateDto> candidates = document.Status == FinancialDocumentStatus.Canceled
            ? []
            : document.Nature switch
            {
                FinancialDocumentNature.Provisional => await ComposeCandidatesAsync(document),
                FinancialDocumentNature.PurchaseReturn when document.IsPurchaseFirmPayable => await NettingCandidatesAsync(document),
                FinancialDocumentNature.SalesReturn when document.IsSalesFirmReceivable => await NettingCandidatesAsync(document),
                _ => [],
            };

        return new FinancialDocumentCompositionDto { Lines = lines, Candidates = candidates };
    }

    private async Task<List<FinancialDocumentCompositionCandidateDto>> ComposeCandidatesAsync(FinancialDocument provisional)
    {
        var open = await OpenContractFirmsAsync(provisional);
        var space = Math.Max(0m, provisional.NetAmount - provisional.ComposedAmount);

        return open
            .Select(d => Candidate(d, d.Nature == FinancialDocumentNature.Firm ? Math.Min(d.OpenAmount, space) : -d.OpenAmount))
            .ToList();
    }

    private async Task<List<FinancialDocumentCompositionCandidateDto>> NettingCandidatesAsync(FinancialDocument refund)
    {
        var firms = (await OpenContractFirmsAsync(refund))
            .Where(d => d.Nature == FinancialDocumentNature.Firm)
            .ToList();
        var firmKeys = firms.Select(f => f.Key).ToList();
        var composed = (await db.Context.FinancialDocumentCompositions.AsNoTracking()
                .Where(c => firmKeys.Contains(c.ComposedDocumentKey))
                .Select(c => c.ComposedDocumentKey)
                .ToListAsync())
            .ToHashSet();

        return firms
            .Where(f => !composed.Contains(f.Key))
            .Select(f => Candidate(f, Math.Min(f.OpenAmount, refund.OpenAmount)))
            .ToList();
    }

    /// <summary>
    /// Firmes e devoluções do MESMO contrato do título-âncora, não cancelados e com saldo. Compra: conciliação e complemento
    /// de valor (D11). Venda (spec 2026-10-15): documento de saída. O título a receber do washout (Firm, outra origem)
    /// não entra.
    /// </summary>
    private async Task<List<FinancialDocument>> OpenContractFirmsAsync(FinancialDocument anchor)
    {
        var documents = db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled && d.NetAmount > d.SettledAmount);

        if (anchor.PurchaseContractKey is { } purchaseKey)
            documents = documents.Where(d => d.PurchaseContractKey == purchaseKey &&
                                             (d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation ||
                                              d.OriginType == FinancialDocumentOrigin.PurchaseInvoice) &&
                                             (d.Nature == FinancialDocumentNature.Firm || d.Nature == FinancialDocumentNature.PurchaseReturn));
        else if (anchor.SalesContractKey is { } salesKey)
            documents = documents.Where(d => d.SalesContractKey == salesKey &&
                                             d.OriginType == FinancialDocumentOrigin.SalesInvoice &&
                                             (d.Nature == FinancialDocumentNature.Firm || d.Nature == FinancialDocumentNature.SalesReturn));
        else
            // Sem contrato não há candidato: comparar com chave nula casaria títulos sem contrato.
            return [];

        return await documents.OrderBy(d => d.DocumentDate).ThenBy(d => d.Code).ToListAsync();
    }

    private static FinancialDocumentCompositionCandidateDto Candidate(FinancialDocument d, decimal suggested) => new()
    {
        DocumentKey = d.Key,
        Code = d.Code,
        Nature = d.Nature.ToString(),
        StorageTransactionCode = d.StorageTransactionCode,
        TaxDocumentNumber = d.TaxDocumentNumber,
        CardCode = d.CardCode,
        CardName = d.CardName,
        DocumentDate = d.DocumentDate,
        OpenAmount = d.OpenAmount,
        SuggestedAmount = suggested,
    };
}
