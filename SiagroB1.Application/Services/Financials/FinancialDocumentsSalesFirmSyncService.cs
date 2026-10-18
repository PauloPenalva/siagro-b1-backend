using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Quem pediu a sincronização: decide o texto da recusa (S1/S2 no documento, S3/S4 na realocação).</summary>
public enum SalesFirmSyncTrigger
{
    Document,
    Reallocation,
}

/// <summary>
/// Título FIRME a receber do documento de saída (spec 2026-10-15): um por LINHA × CONTRATO, derivado do ledger de
/// alocações de venda — a realocação desmembra a linha entre contratos, e o firme acompanha (D7). A cada mudança de
/// estado ou de ledger da linha, compara o DESEJADO com os títulos ATIVOS da linha: cria, recalcula (solto) ou cancela
/// (solto). Composto, ou com dação ou encontro de contas, que precisaria mudar recusa a operação inteira, ANTES de
/// qualquer escrita.
///
/// ENQUEUE-ONLY: quem chama grava. O ledger é lido junto com o que está pendente no contexto (Added entra, Deleted
/// sai), então a ordem em relação ao SaveChanges do chamador não importa. A numeração usa conexão própria (autocommit)
/// e roda dentro da transação do chamador, como a do próprio documento de saída no Expedir e Faturar: um rollback deixa
/// buraco na numeração dos títulos, aceito.
/// </summary>
public class FinancialDocumentsSalesFirmSyncService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FinancialDocumentChangeLogService changeLog,
    FinancialDocumentsFirmSyncService firmSync,
    FinancialDocumentsNettingService netting)
{
    public const string DocumentUndoneReason = "Documento de saída cancelado ou estornado";
    public const string ReallocatedReason = "Volume realocado para outro contrato";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record Desired(Dictionary<Guid, decimal> Amounts, string CancelReason);

    private sealed record LinePlan(SalesInvoiceItem Line, Desired Desired, List<FinancialDocument> Active);

    public async Task EnqueueSyncAsync(
        IReadOnlyCollection<Guid> salesInvoiceItemKeys, string userName,
        SalesFirmSyncTrigger trigger = SalesFirmSyncTrigger.Document)
    {
        var keys = salesInvoiceItemKeys.Distinct().ToList();
        if (keys.Count == 0) return;

        var context = db.Context;
        var lines = await context.SalesInvoicesItems
            .Include(i => i.SalesInvoice)
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToListAsync();

        var plans = new List<LinePlan>();
        foreach (var line in lines)
            plans.Add(new LinePlan(line, await DesiredAsync(line), await ActiveOfAsync(line.Key!.Value)));

        // Regra: recusa ANTES de qualquer escrita — todo título que mudaria precisa estar solto.
        foreach (var plan in plans)
            foreach (var title in plan.Active)
            {
                var stays = plan.Desired.Amounts.TryGetValue(title.SalesContractKey ?? Guid.Empty, out var amount) &&
                            amount == title.NetAmount;
                if (!stays)
                    await EnsureLooseAsync(title, plan.Line.SalesInvoice!.InvoiceNumber, trigger);
            }

        foreach (var plan in plans)
            await ApplyAsync(plan, userName);
    }

    /// <summary>
    /// Só leitura (cancelamento antes da SEFAZ, estorno antes da transação): todo título ativo do documento precisa estar
    /// solto — composto recusa S1, com dação ou encontro de contas recusa S2.
    /// </summary>
    public async Task EnsureCanUndoAsync(SalesInvoice invoice)
    {
        var context = db.Context;
        var lineKeys = (await context.SalesInvoicesItems.AsNoTracking()
                .Where(i => i.SalesInvoiceKey == invoice.Key)
                .Select(i => i.Key)
                .ToListAsync())
            .Where(k => k != null)
            .Select(k => k!.Value)
            .ToList();
        if (lineKeys.Count == 0) return;

        var titles = await context.FinancialDocuments
            .AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice &&
                        d.OriginKey != null && lineKeys.Contains(d.OriginKey.Value) &&
                        d.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();

        foreach (var title in titles)
            await EnsureLooseAsync(title, invoice.InvoiceNumber, SalesFirmSyncTrigger.Document);
    }

    /// <summary>
    /// Modelo do spec: sem volume no ledger, o valor inteiro vai para o contrato da linha; com volume, cada contrato leva
    /// a sua participação (Σ assinada ÷ total), contrato com participação ≤ 0 fica de fora, e o resíduo do arredondamento
    /// vai para a maior participação (empate: o contrato da linha). Devolução tem todos os volumes negativos: o quociente
    /// fica positivo.
    /// </summary>
    internal static Dictionary<Guid, decimal> Distribute(
        decimal value, Guid lineContractKey, IReadOnlyList<(Guid ContractKey, decimal Volume)> volumes)
    {
        var total = volumes.Sum(v => v.Volume);
        if (total == 0m || volumes.All(v => v.Volume == 0m))
            return value > 0m ? new Dictionary<Guid, decimal> { [lineContractKey] = value } : [];

        var shares = volumes
            .GroupBy(v => v.ContractKey)
            .Select(g => (ContractKey: g.Key, Share: g.Sum(v => v.Volume) / total))
            .Where(s => s.Share > 0m)
            .ToList();
        if (shares.Count == 0) return [];

        var amounts = shares.ToDictionary(
            s => s.ContractKey, s => decimal.Round(value * s.Share, 2, MidpointRounding.ToEven));

        var residue = value - amounts.Values.Sum();
        if (residue != 0m)
        {
            var target = shares
                .OrderByDescending(s => s.Share)
                .ThenByDescending(s => s.ContractKey == lineContractKey)
                .First().ContractKey;
            amounts[target] += residue;
        }

        return amounts.Where(a => a.Value > 0m).ToDictionary(a => a.Key, a => a.Value);
    }

    private async Task<Desired> DesiredAsync(SalesInvoiceItem line)
    {
        var invoice = line.SalesInvoice!;

        // G1–G3: confirmado (ou devolvido, que mantém), sem custódia nem remessa para depósito, com contrato. O complemento
        // de ICMS (spec 2026-10-18 S8) nunca gera título, nem com contrato e natureza que gera financeiro na linha.
        if (invoice.InvoiceType == SalesInvoiceType.IcmsComplement ||
            invoice.InvoiceStatus is not (InvoiceStatus.Confirmed or InvoiceStatus.Returned) ||
            invoice.CustodyRole != StorageCustodyRole.None || invoice.ShipmentReleaseKey != null ||
            line.SalesContractKey is null)
            return new Desired([], DocumentUndoneReason);

        // G4: "gera financeiro" da natureza (SAPB1 conta como gera).
        if (!await firmSync.CreatesFinancialDocumentAsync(line.UsageCode))
            return new Desired([], PurchaseFirmPayableRules.NatureWithoutFinancialReason);

        var volumes = await LedgerVolumesAsync(line.Key!.Value);
        return new Desired(Distribute(line.GrandTotal, line.SalesContractKey.Value, volumes), ReallocatedReason);
    }

    /// <summary>Σ do ledger por contrato: o gravado (rastreado) menos o Deleted, mais o Added ainda não gravado.</summary>
    private async Task<List<(Guid ContractKey, decimal Volume)>> LedgerVolumesAsync(Guid lineKey)
    {
        var context = db.Context;
        var saved = await context.SalesContractsAllocations
            .Where(a => a.SalesInvoiceItemKey == lineKey)
            .ToListAsync();

        var entries = context.ChangeTracker.Entries<SalesContractAllocation>()
            .Where(e => e.Entity.SalesInvoiceItemKey == lineKey)
            .ToList();
        var deleted = entries.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity).ToHashSet();
        var added = entries.Where(e => e.State == EntityState.Added).Select(e => e.Entity);

        return saved.Where(a => !deleted.Contains(a))
            .Concat(added.Where(a => !saved.Contains(a)))
            .GroupBy(a => a.SalesContractKey)
            .Select(g => (g.Key, g.Sum(a => a.Volume)))
            .ToList();
    }

    private async Task<List<FinancialDocument>> ActiveOfAsync(Guid lineKey)
    {
        var context = db.Context;
        var saved = await context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice && d.OriginKey == lineKey &&
                        d.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();

        // Título criado antes neste mesmo SaveChanges (Added) ainda não está no banco.
        var pending = context.ChangeTracker.Entries<FinancialDocument>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice && d.OriginKey == lineKey);

        // A instância rastreada pode ter sido cancelada neste mesmo SaveChanges.
        return saved.Concat(pending.Where(d => !saved.Contains(d)))
            .Where(d => d.Status != FinancialDocumentStatus.Canceled)
            .ToList();
    }

    private async Task EnsureLooseAsync(FinancialDocument title, string? invoiceNumber, SalesFirmSyncTrigger trigger)
    {
        var context = db.Context;
        if (PurchaseFirmPayableRules.ProvisionalOf(context, title.Key) is { } provisionalKey)
        {
            var provisionalCode = await context.FinancialDocuments.AsNoTracking()
                .Where(d => d.Key == provisionalKey).Select(d => d.Code).FirstOrDefaultAsync();
            throw new DefaultException(trigger == SalesFirmSyncTrigger.Reallocation
                ? $"O título {title.Code} da linha realocada já está composto no título {provisionalCode}: desfaça a composição antes de realocar."
                : $"O título {title.Code} do documento de saída {invoiceNumber} já está composto no título {provisionalCode}: desfaça a composição antes de cancelar ou estornar o documento.");
        }

        if (title.SettledAmount != 0m)
            throw new DefaultException(trigger == SalesFirmSyncTrigger.Reallocation
                ? $"O título {title.Code} da linha realocada já tem baixa por dação ou encontro de contas: estorne-a antes de realocar."
                : $"O título {title.Code} do documento de saída {invoiceNumber} já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.");
    }

    private async Task ApplyAsync(LinePlan plan, string userName)
    {
        var context = db.Context;
        var now = DateTime.Now;

        foreach (var title in plan.Active)
        {
            if (!plan.Desired.Amounts.TryGetValue(title.SalesContractKey ?? Guid.Empty, out var amount))
            {
                FinancialDocumentsCancelService.Cancel(title, plan.Desired.CancelReason, userName);
                continue;
            }

            if (title.NetAmount == amount) continue;

            changeLog.Register(title.Key, FinancialDocumentChangeLogFields.NetAmount,
                title.NetAmount.ToString("N2", PtBr), amount.ToString("N2", PtBr), userName);
            title.NetAmount = amount;
            title.UpdatedAt = now;
            title.UpdatedBy = userName;
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, title.Key);
        }

        var existing = plan.Active.Select(t => t.SalesContractKey ?? Guid.Empty).ToHashSet();
        var missing = plan.Desired.Amounts.Keys.Where(k => !existing.Contains(k)).ToList();
        if (missing.Count == 0) return;

        var line = plan.Line;
        var invoice = line.SalesInvoice!;
        var codes = await context.SalesContracts.AsNoTracking()
            .Where(c => missing.Contains(c.Key))
            .Select(c => new { c.Key, c.Code })
            .ToDictionaryAsync(c => c.Key, c => c.Code);
        var nature = invoice.InvoiceType == SalesInvoiceType.Return
            ? FinancialDocumentNature.SalesReturn
            : FinancialDocumentNature.Firm;
        var issueDate = (invoice.InvoiceDate ?? DateTime.Today).Date;
        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);

        foreach (var contractKey in missing)
        {
            var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
            var code = await docNumberSequence.GetDocNumber(docNumberKey);

            var document = new FinancialDocument
            {
                Key = Guid.NewGuid(),
                Code = code,
                DocNumberKey = docNumberKey,
                BranchCode = invoice.BranchCode,
                Direction = FinancialDirection.Receivable,
                Nature = nature,
                Status = FinancialDocumentStatus.Open,
                CardCode = invoice.CardCode,
                CardName = invoice.CardName,
                // Firme não é cobrado direto: o vencimento que vale é o do provisório.
                DocumentDate = issueDate,
                DueDate = issueDate,
                Currency = CurrencyType.Brl,
                NetAmount = plan.Desired.Amounts[contractKey],
                OriginType = FinancialDocumentOrigin.SalesInvoice,
                OriginKey = line.Key,
                OriginDocNumber = codes.GetValueOrDefault(contractKey),
                SalesContractKey = contractKey,
                SalesInvoiceKey = invoice.Key,
                TaxDocumentNumber = taxDocument,
                CreatedAt = now,
                CreatedBy = userName,
                UpdatedAt = now,
                UpdatedBy = userName,
            };

            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);

            // Regra 9: a devolução nascida compensa o firme solto da linha de origem, no mesmo contrato.
            if (nature == FinancialDocumentNature.SalesReturn)
                await netting.EnqueueAutoForSalesAsync(document, line.SalesInvoiceItemOriginKey, userName);
        }
    }
}
