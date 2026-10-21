using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.FiscalStock;

/// <summary>
/// Fechamento e reabertura do mês do estoque fiscal (STANDALONE). Fechar congela as camadas PEPS abertas no
/// último dia do mês (o instantâneo que semeia os meses seguintes); só fecha em ordem, a partir do mês do saldo
/// inicial mais antigo, e só se todo produto fecha limpo — sem saída a descoberto, valor pendente ou resíduo —
/// para que o instantâneo seja exatamente o saldo. Reabrir só o último fechado, com motivo.
/// </summary>
public class FiscalStockPeriodsService(IUnitOfWork db, FiscalStockLedgerLoader? ledgerLoader = null)
    : IFiscalStockPeriodsService
{
    private readonly FiscalStockLedgerLoader _ledgerLoader = ledgerLoader ?? new FiscalStockLedgerLoader(db);

    private const int MaxListedItems = 20;
    private static readonly CultureInfo PtBr = new("pt-BR");

    public IQueryable<FiscalStockPeriod> QueryAll() => db.Context.FiscalStockPeriods.AsNoTracking();

    public async Task<FiscalStockPeriod> CloseAsync(string branchCode, int year, int month, string userName)
    {
        var first = ValidateMonth(branchCode, year, month);
        var period = await FindAsync(branchCode, year, month);
        if (period?.Status == FiscalStockPeriodStatus.Closed)
            throw new DefaultException($"O estoque fiscal de {first:MM/yyyy} já está fechado.");

        var oldestOpening = await db.Context.FiscalInventoryOpeningBalances
            .Where(o => o.BranchCode == branchCode)
            .OrderBy(o => o.BalanceDate).Select(o => (DateTime?)o.BalanceDate).FirstOrDefaultAsync();
        if (oldestOpening == null)
            throw new DefaultException("Lance o Saldo Inicial do Estoque antes de fechar.");

        var firstClosable = new DateTime(oldestOpening.Value.Year, oldestOpening.Value.Month, 1);
        if (first < firstClosable)
            throw new DefaultException(
                $"O estoque fiscal começa em {firstClosable:MM/yyyy} (saldo inicial mais antigo); não há mês para fechar antes disso.");

        if (first > firstClosable)
        {
            var previous = first.AddMonths(-1);
            if (!await IsClosedAsync(branchCode, previous.Year, previous.Month))
                throw new DefaultException($"Feche antes {previous:MM/yyyy}.");
        }

        var lastDay = first.AddMonths(1).AddDays(-1);
        var ledgers = await _ledgerLoader.LoadAsync(branchCode, itemCode: null, toDate: lastDay);
        EnsureClean(ledgers);

        await db.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            if (period == null)
            {
                period = new FiscalStockPeriod { BranchCode = branchCode, Year = year, Month = month };
                await db.Context.FiscalStockPeriods.AddAsync(period);
            }

            period.Status = FiscalStockPeriodStatus.Closed;
            period.ClosedAt = now;
            period.ClosedBy = userName;
            db.Context.FiscalStockPeriodEvents.Add(new FiscalStockPeriodEvent
                { Period = period, EventType = FiscalStockPeriodEventType.Closed, At = now, By = userName });

            foreach (var ledger in ledgers)
            {
                var sequence = 0;
                foreach (var layer in ledger.Result.OpenLayers)
                    db.Context.FiscalStockPeriodLayers.Add(new FiscalStockPeriodLayer
                    {
                        Period = period, ItemCode = ledger.ItemCode, ItemName = ledger.ItemName,
                        UnitOfMeasureCode = ledger.UnitOfMeasureCode, LayerDate = layer.Date,
                        SourceLineKey = layer.LineKey, Quantity = layer.Quantity, Value = layer.Value,
                        Sequence = ++sequence,
                    });
            }

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return period;
    }

    public async Task<FiscalStockPeriod> ReopenAsync(string branchCode, int year, int month, string? reason, string userName)
    {
        var first = ValidateMonth(branchCode, year, month);
        if (string.IsNullOrWhiteSpace(reason))
            throw new DefaultException("Informe o motivo da reabertura.");
        reason = reason.Trim();

        var period = await FindAsync(branchCode, year, month);
        if (period?.Status != FiscalStockPeriodStatus.Closed)
            throw new DefaultException($"O estoque fiscal de {first:MM/yyyy} não está fechado.");

        var next = first.AddMonths(1);
        if (await IsClosedAsync(branchCode, next.Year, next.Month))
            throw new DefaultException($"Reabra antes {next:MM/yyyy}.");

        await db.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var layers = await db.Context.FiscalStockPeriodLayers.Where(l => l.PeriodKey == period.Key).ToListAsync();
            db.Context.FiscalStockPeriodLayers.RemoveRange(layers);

            period.Status = FiscalStockPeriodStatus.Open;
            period.ReopenedAt = now;
            period.ReopenedBy = userName;
            period.ReopenReason = reason;
            db.Context.FiscalStockPeriodEvents.Add(new FiscalStockPeriodEvent
            {
                PeriodKey = period.Key, EventType = FiscalStockPeriodEventType.Reopened, At = now, By = userName,
                Reason = reason,
            });

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return period;
    }

    private static DateTime ValidateMonth(string branchCode, int year, int month)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new DefaultException("Informe a filial.");
        if (year is < 2000 or > 2999 || month is < 1 or > 12)
            throw new DefaultException("Informe um mês válido.");
        return new DateTime(year, month, 1);
    }

    private Task<FiscalStockPeriod?> FindAsync(string branchCode, int year, int month) =>
        db.Context.FiscalStockPeriods.FirstOrDefaultAsync(p =>
            p.BranchCode == branchCode && p.Year == year && p.Month == month);

    private Task<bool> IsClosedAsync(string branchCode, int year, int month) =>
        db.Context.FiscalStockPeriods.AnyAsync(p =>
            p.BranchCode == branchCode && p.Year == year && p.Month == month &&
            p.Status == FiscalStockPeriodStatus.Closed);

    /// <summary>
    /// Todo produto precisa fechar com o saldo igual às camadas abertas: sem saída a descoberto (Deficit),
    /// complemento sem camada (PendingValue), valor sem camada (ResidualValue) ou quantidade 0 com valor.
    /// </summary>
    private static void EnsureClean(List<FiscalStockItemLedger> ledgers)
    {
        var dirty = ledgers
            .Select(l => (Ledger: l,
                Quantity: l.Result.OpenLayers.Sum(x => x.Quantity) - l.Result.Deficit,
                Value: l.Result.OpenLayers.Sum(x => x.Value) + l.Result.ResidualValue + l.Result.PendingValue))
            .Where(x => x.Ledger.Result.Deficit > 0 || x.Ledger.Result.PendingValue != 0 ||
                        x.Ledger.Result.ResidualValue != 0 || (x.Quantity == 0 && x.Value != 0))
            .ToList();
        if (dirty.Count == 0) return;

        var lines = dirty.Take(MaxListedItems).Select(x =>
            $"{x.Ledger.ItemCode} {x.Ledger.ItemName}: saldo {x.Quantity.ToString("#,##0.######", PtBr)} / R$ {x.Value.ToString("N2", PtBr)}");
        var more = dirty.Count > MaxListedItems ? $"\n(e mais {dirty.Count - MaxListedItems} produtos)" : "";
        throw new DefaultException(
            "Não é possível fechar o mês: há produtos com saída sem saldo ou valor sem quantidade em estoque. " +
            "Lance o documento de entrada ou um ajuste manual antes de fechar.\n" +
            string.Join("\n", lines) + more);
    }
}
