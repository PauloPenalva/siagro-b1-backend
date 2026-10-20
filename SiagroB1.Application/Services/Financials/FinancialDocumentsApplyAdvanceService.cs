using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Compensa o crédito de um adiantamento contra o PROVISÓRIO do mesmo contrato de compra (spec 2026-10-14, regras 1–4).
/// Manual (D1): o operador escolhe adiantamento e valor. Em PAR (D4), como o encontro de contas: uma linha
/// <see cref="FinancialSettlementOrigin.AdvanceApplication"/> em cada título, sem conta, com RelatedDocumentKey cruzado.
/// No provisório ela entra no baixado como qualquer baixa; no adiantamento, no APLICADO.
///
/// Tetos: o disponível para baixa do provisório (composto − baixado — só se compensa o que já está coberto por
/// documento de entrada) e o crédito do adiantamento (pago − aplicado). Enqueue-only até o SaveChanges final; o
/// RowVersion dos dois títulos protege a dupla compensação simultânea do mesmo crédito.
/// </summary>
public class FinancialDocumentsApplyAdvanceService(IUnitOfWork db, FinancialDocumentChangeLogService changeLog)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task ExecuteAsync(
        Guid provisionalKey, Guid advanceKey, decimal amount, DateTime settlementDate, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var context = db.Context;

        var provisional = await context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == provisionalKey)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (provisional.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException("Título cancelado não pode ser compensado.");

        var advance = await context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == advanceKey)
                      ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (advance.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException($"O título {advance.Code} não é um adiantamento.");

        if (advance.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException("Título cancelado não pode ser compensado.");

        if (advance.PurchaseContractKey is not null || advance.SalesContractKey is not null)
        {
            // Mesmo CONTRATO: adiantamento de outro contrato se revincula antes (fora do escopo compensar entre
            // contratos). Título sem contrato também "não pertence ao contrato".
            if (!advance.HasSameContractAs(provisional))
                throw new ApplicationException(
                    $"Adiantamento {advance.Code} não pertence ao contrato do título {provisional.Code}.");

            if (provisional.Nature != FinancialDocumentNature.Provisional)
                throw new ApplicationException($"Título {provisional.Code} não é provisório de contrato de compra.");
        }
        else
        {
            // Spec 2026-10-19 (decisão 1): adiantamento SEM contrato abate qualquer título do mesmo parceiro, direção e
            // moeda — provisório, firme ou manual —, menos outro adiantamento.
            if (provisional.Nature == FinancialDocumentNature.Advance)
                throw new ApplicationException(
                    $"O título {provisional.Code} é um adiantamento: adiantamento não abate outro adiantamento.");

            if (!string.Equals(provisional.CardCode, advance.CardCode, StringComparison.OrdinalIgnoreCase))
                throw new ApplicationException(
                    $"O título {provisional.Code} é de outro parceiro ({provisional.CardCode}); o adiantamento {advance.Code} é de {advance.CardCode}.");

            if (provisional.Direction != advance.Direction)
                throw new ApplicationException(
                    $"O título {provisional.Code} e o adiantamento {advance.Code} têm direção diferente (a pagar x a receber).");
        }

        if (advance.Currency != provisional.Currency)
            throw new ApplicationException(
                $"O adiantamento {advance.Code} está em {Currency(advance.Currency)} e o título {provisional.Code} em {Currency(provisional.Currency)}: compensação entre moedas diferentes não é suportada.");

        // O Edm.Double pode trazer mais de duas casas: arredonda ANTES dos tetos.
        var value = decimal.Round(amount, 2, MidpointRounding.ToEven);

        if (value <= 0m)
            throw new ApplicationException("Informe um valor maior que zero.");

        if (value > provisional.AvailableToSettleAmount)
            throw new ApplicationException(
                $"Valor acima do disponível para baixa do título {provisional.Code} ({provisional.AvailableToSettleAmount.ToString("N2", PtBr)}).");

        if (value > advance.AvailableAdvanceAmount)
            throw new ApplicationException(
                $"Valor acima do crédito disponível do adiantamento {advance.Code} ({advance.AvailableAdvanceAmount.ToString("N2", PtBr)}).");

        var now = DateTime.Now;
        context.FinancialSettlements.AddRange(
            Line(provisional, advance, value, settlementDate.Date, now, userName),
            Line(advance, provisional, value, settlementDate.Date, now, userName));

        changeLog.Register(provisional.Key, FinancialDocumentChangeLogFields.AdvanceApplication, null,
            FinancialDocumentsComposeService.Describe(advance.Code, value), userName);
        changeLog.Register(advance.Key, FinancialDocumentChangeLogFields.AdvanceApplication, null,
            FinancialDocumentsComposeService.Describe(provisional.Code, value), userName);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, provisional.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, advance.Key);

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }

    private static string Currency(CurrencyType currency) =>
        FinancialDocumentsContractCancellationGuardService.CurrencySymbol(currency);

    private static FinancialSettlement Line(
        FinancialDocument document, FinancialDocument other, decimal amount, DateTime settlementDate, DateTime now,
        string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = document.Key,
        FinancialAccountCode = null,
        SettlementDate = settlementDate,
        Amount = amount,
        Origin = FinancialSettlementOrigin.AdvanceApplication,
        RelatedDocumentKey = other.Key,
        DocumentReference = other.Code,
        CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
    };
}
