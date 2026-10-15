using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Candidatos do "Compensar adiantamento" (spec 2026-10-14, regras 2 e 4): adiantamentos não cancelados do MESMO
/// contrato de compra e MESMA moeda do provisório, com crédito > 0, com o valor sugerido. Título que não é provisório
/// de compra ativo devolve lista vazia — a tela só oferece o botão no provisório. Projeção em memória porque
/// AvailableAdvanceAmount/AvailableToSettleAmount são [NotMapped].
/// </summary>
public class FinancialDocumentsGetAdvanceCandidatesService(IUnitOfWork db)
{
    public async Task<List<FinancialDocumentAdvanceCandidateDto>> ExecuteAsync(Guid key)
    {
        var provisional = await db.Context.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (provisional.Nature != FinancialDocumentNature.Provisional ||
            provisional.Status == FinancialDocumentStatus.Canceled ||
            (provisional.PurchaseContractKey is null && provisional.SalesContractKey is null))
            return [];

        var available = provisional.AvailableToSettleAmount;

        var advances = await db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.PurchaseContractKey == provisional.PurchaseContractKey &&
                        d.SalesContractKey == provisional.SalesContractKey &&
                        d.Nature == FinancialDocumentNature.Advance &&
                        d.Status != FinancialDocumentStatus.Canceled &&
                        d.Currency == provisional.Currency &&
                        d.SettledAmount - d.AppliedAmount > 0m)
            .OrderBy(d => d.DocumentDate).ThenBy(d => d.Code)
            .ToListAsync();

        return advances.Select(d => new FinancialDocumentAdvanceCandidateDto
        {
            DocumentKey = d.Key,
            Code = d.Code,
            DocumentDate = d.DocumentDate,
            SettledAmount = d.SettledAmount,
            AppliedAmount = d.AppliedAmount,
            AvailableAdvanceAmount = d.AvailableAdvanceAmount,
            SuggestedAmount = Math.Min(available, d.AvailableAdvanceAmount),
        }).ToList();
    }
}
