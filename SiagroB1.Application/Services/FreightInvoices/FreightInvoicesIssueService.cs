using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>
/// Emite a fatura de frete por AGLUTINAÇÃO: as parcelas em aberto dos títulos dos CT-e são baixadas com origem
/// <see cref="FinancialSettlementOrigin.Aggregation"/> (sem conta, sem dinheiro, sem contabilização — a receita e o
/// cliente já foram lançados nos títulos dos CT-e) e nasce o(s) título(s) a receber da fatura (origem
/// <see cref="FinancialDocumentOrigin.FreightInvoice"/>, também fora da contabilização de inclusão).
/// Tudo numa transação; a revalidação roda depois do BeginTransaction. A corrida entre duas emissões é barrada pelo
/// RowVersion da fatura (e dos títulos dos CT-e) no SaveChanges.
/// </summary>
public class FreightInvoicesIssueService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FreightInvoiceEligibleCtesQuery eligible,
    IConfiguration configuration)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task IssueAsync(Guid invoiceKey, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);

        await db.BeginTransactionAsync();
        try
        {
            await IssueCoreAsync(invoiceKey, user);
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            db.Context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task IssueCoreAsync(Guid invoiceKey, string user)
    {
        var context = db.Context;
        var invoice = await context.FreightInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Key == invoiceKey)
                      ?? throw new DefaultException("Fatura de frete não encontrada.");
        if (invoice.Status != FreightInvoiceStatus.Draft)
            throw new DefaultException("Só o rascunho da fatura pode ser emitido.");

        var items = invoice.Items.Where(i => i.IsActive).OrderBy(i => i.LineNum).ToList();
        if (items.Count == 0) throw new DefaultException("A fatura não tem CT-e.");

        // 1. Revalida: nada baixado, cancelado ou faturado em outra fatura desde o rascunho.
        var rows = (await eligible.ListAsync(invoice.BranchCode, invoice.TakerCardCode, null, null, invoice.Key))
            .ToDictionary(r => r.TransportDocumentKey);
        foreach (var item in items.Where(i => !rows.ContainsKey(i.TransportDocumentKey)))
        {
            var number = await context.TransportDocuments.Where(d => d.Key == item.TransportDocumentKey)
                .Select(d => d.Number).FirstOrDefaultAsync();
            throw new DefaultException(
                $"CT-e {number} não pode ser faturado: não está autorizado, já está em outra fatura, não gera título ou possui baixa.");
        }

        var keys = items.Select(i => i.TransportDocumentKey).ToList();
        var titles = await context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.TransportDocument && d.Status != FinancialDocumentStatus.Canceled
                        && d.OriginKey != null && keys.Contains(d.OriginKey.Value))
            .ToListAsync();

        var total = titles.Sum(t => t.OpenAmount);
        if (total != invoice.TotalAmount)
            throw new DefaultException(
                $"O saldo dos títulos dos CT-e ({total.ToString("N2", PtBr)}) difere do total da fatura ({invoice.TotalAmount.ToString("N2", PtBr)}): salve a fatura novamente.");
        if (total <= 0m) throw new DefaultException("A fatura não tem valor a receber.");

        var now = DateTime.Now;
        var issueDate = invoice.IssueDate.Date;

        // 2. Aglutinação: baixa sem conta. NÃO enfileira contabilização (SettlementPosting também exclui a origem).
        // [settlement-posting: not applicable] — troca título por título do mesmo cliente; nada a lançar.
        foreach (var title in titles.Where(t => t.OpenAmount > 0m))
        {
            context.FinancialSettlements.Add(new FinancialSettlement
            {
                Key = Guid.NewGuid(),
                FinancialDocumentKey = title.Key,
                FinancialAccountCode = null,
                SettlementDate = issueDate,
                Amount = title.OpenAmount,
                Origin = FinancialSettlementOrigin.Aggregation,
                RelatedDocumentKey = invoice.Key,
                DocumentReference = invoice.Code,
                Notes = $"Fatura de frete {invoice.Code}",
                CreatedAt = now, CreatedBy = user, UpdatedAt = now, UpdatedBy = user,
            });
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, title.Key);
        }

        // 3. Título(s) da fatura: pela condição de pagamento, ou vencimento único.
        var firstCte = keys[0];
        var dims = titles.Where(t => t.OriginKey == firstCte).OrderBy(t => t.InstallmentNumber).First();
        var plan = invoice.PaymentConditionCode is not null
            ? await PaymentInstallmentPlanner.PlanAsync(context, invoice.PaymentConditionCode, total, issueDate)
            : [(1, (invoice.DueDate ?? issueDate).Date, total)];

        foreach (var (installment, dueDate, value) in plan)
        {
            var assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
                TransactionCode.FinancialDocument, null, null, 50,
                c => context.FinancialDocuments.AnyAsync(x => x.Code == c)));
            var document = new FinancialDocument
            {
                Key = Guid.NewGuid(),
                Code = assignment.Code,
                DocNumberKey = assignment.DocNumberKey,
                BranchCode = invoice.BranchCode,
                Direction = FinancialDirection.Receivable,
                Nature = FinancialDocumentNature.Firm,
                Status = FinancialDocumentStatus.Open,
                CardCode = invoice.TakerCardCode,
                CardName = invoice.TakerName ?? dims.CardName,
                DocumentDate = issueDate,
                DueDate = dueDate,
                Currency = CurrencyType.Brl,
                NetAmount = value,
                InstallmentNumber = installment,
                OriginType = FinancialDocumentOrigin.FreightInvoice,
                OriginKey = invoice.Key,
                OriginDocNumber = invoice.Code,
                CostCenterCode = dims.CostCenterCode,
                AccountingItemCode = dims.AccountingItemCode,
                DistributionRuleCode = dims.DistributionRuleCode,
                LedgerAccountCode = dims.LedgerAccountCode,
                CreatedAt = now, CreatedBy = user, UpdatedAt = now, UpdatedBy = user,
            };
            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
        }

        // 4.
        invoice.Status = FreightInvoiceStatus.Issued;
        invoice.IssuedAt = now;
    }
}
