using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Campos alterados na edição do título manual; nulo = não mexer.</summary>
public sealed class ManualFinancialDocumentChanges
{
    public string? CardCode { get; init; }
    public string? BranchCode { get; init; }
    public DateTime? DocumentDate { get; init; }
    public DateTime? DueDate { get; init; }
    public CurrencyType? Currency { get; init; }
    public decimal? NetAmount { get; init; }

    // Textos: o Has* indica alteração, porque o valor novo pode ser vazio.
    public bool HasTaxDocumentNumber { get; init; }
    public string? TaxDocumentNumber { get; init; }
    public bool HasPaymentTermsText { get; init; }
    public string? PaymentTermsText { get; init; }
    public bool HasComments { get; init; }
    public string? Comments { get; init; }

    // Amarração contábil: nulo = não mexer; vazio = limpar.
    public string? CostCenterCode { get; init; }
    public string? AccountingItemCode { get; init; }
    public string? DistributionRuleCode { get; init; }
    public string? LedgerAccountCode { get; init; }
}

/// <summary>
/// Edição do título lançado à mão. Número e série nunca mudam. Com qualquer movimento de baixa (mesmo estornado), só
/// vencimento e textos mudam: parceiro, valor, moeda, filial e emissão ficam presos ao que foi baixado. Cada campo
/// alterado vai para o log do título.
/// </summary>
public class FinancialDocumentsManualUpdateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    FinancialDocumentChangeLogService changeLog,
    AccountingAssignmentValidator? dimensionValidator = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<FinancialDocument> ExecuteAsync(Guid key, ManualFinancialDocumentChanges changes, string userName)
    {
        var document = await db.Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Título financeiro não encontrado.");

        if (!FinancialDocumentsManualRules.IsManualTitle(document))
            throw new ApplicationException(
                $"O título {document.Code} foi gerado automaticamente e não pode ser editado.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O título {document.Code} está cancelado e não pode ser editado.");

        var hasSettlements = document.SettledAmount != 0m ||
                             await db.Context.FinancialSettlements.AnyAsync(x =>
                                 x.FinancialDocumentKey == key || x.RelatedDocumentKey == key);

        var cardCode = changes.CardCode?.Trim();
        var branchCode = changes.BranchCode?.Trim();
        var amount = changes.NetAmount is { } a ? decimal.Round(a, 2, MidpointRounding.ToEven) : (decimal?)null;

        var lockedChange =
            (cardCode is not null && cardCode != document.CardCode) ||
            (branchCode is not null && branchCode != document.BranchCode) ||
            (changes.DocumentDate is { } dd && dd.Date != document.DocumentDate.Date) ||
            (changes.Currency is { } c && c != document.Currency) ||
            (amount is { } am && am != document.NetAmount);

        if (hasSettlements && lockedChange)
            throw new ApplicationException(
                $"O título {document.Code} possui baixa financeira: só vencimento, documento, condição e observações podem ser alterados.");

        var newCard = cardCode ?? document.CardCode;
        var newBranch = branchCode ?? document.BranchCode;
        var newDocumentDate = changes.DocumentDate ?? document.DocumentDate;
        var newDueDate = changes.DueDate ?? document.DueDate;
        var newAmount = amount ?? document.NetAmount;

        FinancialDocumentsManualRules.Validate(newCard, newBranch, newDocumentDate, newDueDate, newAmount);

        if (newCard != document.CardCode)
        {
            var partner = await businessPartnerService.GetByIdAsync(newCard)
                          ?? throw new ApplicationException($"Parceiro de negócio {newCard} não encontrado.");
            Log(document, FinancialDocumentChangeLogFields.Partner, document.CardCode, newCard, userName);
            document.CardCode = newCard;
            document.CardName = partner.CardName;
        }

        if (newBranch != document.BranchCode)
        {
            Log(document, FinancialDocumentChangeLogFields.Branch, document.BranchCode, newBranch, userName);
            document.BranchCode = newBranch;
        }

        if (newDocumentDate.Date != document.DocumentDate.Date)
        {
            Log(document, FinancialDocumentChangeLogFields.DocumentDate, Date(document.DocumentDate), Date(newDocumentDate), userName);
            document.DocumentDate = newDocumentDate;
        }

        if (newDueDate.Date != document.DueDate.Date)
        {
            Log(document, FinancialDocumentChangeLogFields.DueDate, Date(document.DueDate), Date(newDueDate), userName);
            document.DueDate = newDueDate;
        }

        if (changes.Currency is { } currency && currency != document.Currency)
        {
            Log(document, FinancialDocumentChangeLogFields.Currency, document.Currency.ToString(), currency.ToString(), userName);
            document.Currency = currency;
        }

        if (newAmount != document.NetAmount)
        {
            Log(document, FinancialDocumentChangeLogFields.NetAmount,
                document.NetAmount.ToString("N2", PtBr), newAmount.ToString("N2", PtBr), userName);
            document.NetAmount = newAmount;
        }

        if (changes.HasTaxDocumentNumber)
            document.TaxDocumentNumber = Text(document, FinancialDocumentChangeLogFields.TaxDocumentNumber,
                document.TaxDocumentNumber, changes.TaxDocumentNumber, userName);

        if (changes.HasPaymentTermsText)
            document.PaymentTermsText = Text(document, FinancialDocumentChangeLogFields.PaymentTerms,
                document.PaymentTermsText, changes.PaymentTermsText, userName);

        if (changes.HasComments)
            document.Comments = Text(document, FinancialDocumentChangeLogFields.Comments,
                document.Comments, changes.Comments, userName);

        await ApplyDimensionsAsync(document, changes);

        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        await db.Context.SaveChangesAsync();
        return document;
    }

    // Só os campos enviados mudam; o validador compara com o gravado, então centro inativado depois de usado
    // continua aceito no título antigo e só um código novo é recusado.
    private async Task ApplyDimensionsAsync(FinancialDocument document, ManualFinancialDocumentChanges changes)
    {
        var previous = AccountingAssignmentSnapshot.FromOriginal(n => db.Context.Entry(document).OriginalValues[n]);
        var target = AccountingAssignmentSnapshot.From(document);

        if (changes.CostCenterCode is not null) target.CostCenterCode = changes.CostCenterCode;
        if (changes.AccountingItemCode is not null) target.AccountingItemCode = changes.AccountingItemCode;
        if (changes.DistributionRuleCode is not null) target.DistributionRuleCode = changes.DistributionRuleCode;

        // Troca de modo: quem informa só um lado limpa o outro (rateio x CC/Item nunca ficam juntos). Se os dois
        // lados vierem preenchidos, o validador recusa.
        var typedRule = !string.IsNullOrWhiteSpace(changes.DistributionRuleCode);
        var typedCcItem = !string.IsNullOrWhiteSpace(changes.CostCenterCode)
                          || !string.IsNullOrWhiteSpace(changes.AccountingItemCode);
        if (typedRule && changes.CostCenterCode is null && changes.AccountingItemCode is null)
        {
            target.CostCenterCode = null;
            target.AccountingItemCode = null;
        }
        else if (typedCcItem && changes.DistributionRuleCode is null)
        {
            target.DistributionRuleCode = null;
        }

        if (changes.LedgerAccountCode is not null) target.LedgerAccountCode = changes.LedgerAccountCode;

        if (dimensionValidator is not null)
            await dimensionValidator.NormalizeAndValidateAsync(target, previous, document.DocumentDate);

        AccountingAssignmentSnapshot.Copy(target, document);
    }

    private string? Text(FinancialDocument document, string field, string? current, string? value, string userName)
    {
        var clean = FinancialDocumentsManualRules.Clean(value);
        if (clean != current)
            Log(document, field, current, clean, userName);
        return clean;
    }

    private void Log(FinancialDocument document, string field, string? oldValue, string? newValue, string userName) =>
        changeLog.Register(document.Key, field, oldValue, newValue, userName);

    private static string Date(DateTime value) => value.ToString("dd/MM/yyyy", PtBr);
}
