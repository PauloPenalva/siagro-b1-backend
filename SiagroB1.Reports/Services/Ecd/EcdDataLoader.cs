using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Services.Ecd;

/// <summary>
/// Lê do banco a ECD do ano: escrituração centralizada na matriz, com os lançamentos de todas as filiais da raiz.
/// Saldo de abertura pela mesma leitura do Balancete/Razão (<see cref="AccountPostingReader"/>): tudo até 31/12
/// do ano anterior, inclusive o saldo inicial lançado na data de corte. Estorno é um lançamento a mais (não há
/// status de cancelado), então entra como nos relatórios e se anula com o original.
/// </summary>
public class EcdDataLoader(IUnitOfWork db)
{
    public async Task<EcdData> LoadAsync(EcdRequest request)
    {
        var (branches, declarant) = await CompanyBranches.LoadAsync(db, request.BranchCode);
        var codes = branches.Select(b => b.Code).ToHashSet();
        var start = new DateTime(request.Year, 1, 1);
        var end = new DateTime(request.Year, 12, 31);

        // Leitura sem filtro de filial e recorte em memória: o leitor dos relatórios aceita uma filial só.
        var previous = await new AccountPostingReader(db).ReadAsync(null, start.AddDays(-1), new AccountPostingFilter());
        var opening = previous.Where(p => codes.Contains(p.BranchCode))
            .GroupBy(p => (Account: p.AccountCode, CostCenter: p.CostCenterCode))
            .Select(g => (g.Key, Balance: g.Sum(p => p.Debit - p.Credit)))
            .Where(x => x.Balance != 0)
            .ToDictionary(x => x.Key, x => x.Balance);

        var entries = await db.Context.JournalEntries.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.PostingDate >= start && e.PostingDate <= end && codes.Contains(e.BranchCode))
            .OrderBy(e => e.PostingDate).ThenBy(e => e.DocEntry)
            .ToListAsync();
        foreach (var e in entries) e.Lines = e.Lines.OrderBy(l => l.LineNumber).ToList();

        var views = await db.Context.ManagementViews.AsNoTracking().Include(v => v.Lines)
            .OrderBy(v => v.Code).ToListAsync();

        return new EcdData
        {
            Declarant = declarant,
            Branches = branches,
            MunicipalityName = declarant?.MunicipalityCode == null ? null
                : await db.Context.Municipalities.AsNoTracking().Where(m => m.Code == declarant.MunicipalityCode)
                    .Select(m => m.Name).FirstOrDefaultAsync(),
            Settings = declarant == null ? null
                : await db.Context.EcdSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == declarant.Code),
            Accountant = declarant == null ? null
                : await db.Context.SpedFiscalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == declarant.Code),
            Accounting = await db.Context.AccountingSettings.AsNoTracking().FirstOrDefaultAsync() ?? new AccountingSettings(),
            Accounts = await db.Context.LedgerAccounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync(),
            CostCenters = await db.Context.CostCenters.AsNoTracking().OrderBy(c => c.Code).ToListAsync(),
            Entries = entries,
            OpeningBalances = opening,
            Periods = await db.Context.AccountingPeriods.AsNoTracking().Where(p => p.Year == request.Year)
                .OrderBy(p => p.Month).ToListAsync(),
            // Não há visão "da ECD" no cadastro: vale a primeira de cada tipo pelo código.
            BalanceSheet = views.FirstOrDefault(v => v.Type == ManagementViewType.BalanceSheet),
            IncomeStatement = views.FirstOrDefault(v => v.Type == ManagementViewType.IncomeStatement),
        };
    }
}
