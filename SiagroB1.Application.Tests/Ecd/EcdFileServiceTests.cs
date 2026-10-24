using System.Globalization;
using System.Text;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Reports.Services.Ecd;

namespace SiagroB1.Application.Tests.Ecd;

public class EcdFileServiceTests
{
    private static readonly CultureInfo Pt = new("pt-BR");

    // ---- cenário ----

    private static JournalEntryLine Dbl(string d, string c, decimal v, string? ccd = null, string? ccc = null, string h = "H") => new()
    {
        EntryType = JournalEntryLineType.Double, DebitAccountCode = d, CreditAccountCode = c, Amount = v, History = h,
        DebitCostCenterCode = ccd, CreditCostCenterCode = ccc,
    };

    private static JournalEntryLine Deb(string d, decimal v, string? cc = null, string h = "H") => new()
        { EntryType = JournalEntryLineType.Debit, DebitAccountCode = d, Amount = v, History = h, DebitCostCenterCode = cc };

    private static JournalEntryLine Cre(string c, decimal v, string h = "H") => new()
        { EntryType = JournalEntryLineType.Credit, CreditAccountCode = c, Amount = v, History = h };

    private static JournalEntryLine Cont(string h) => new() { EntryType = JournalEntryLineType.HistoryContinuation, History = h };

    private static JournalEntry Entry(DateTime date, params JournalEntryLine[] lines) => Entry(date, null, lines);

    private static JournalEntry Entry(DateTime date, string? source, params JournalEntryLine[] lines)
    {
        for (var i = 0; i < lines.Length; i++) lines[i].LineNumber = i + 1;
        return new JournalEntry
        {
            PostingDate = date, BranchCode = "01", Batch = "000001", DocumentNumber = "000001", Origin = JournalEntryOrigin.Manual,
            CreatedBy = "t", SourceDocumentType = source, Lines = lines.ToList(),
        };
    }

    private static LedgerAccount Account(string code, string name, LedgerAccountType type, string? parent, bool analytic = true,
        string? referential = "REF") => new()
    {
        Code = code, Name = name, Type = type, ParentCode = parent, AllowsPosting = analytic,
        Nature = type is LedgerAccountType.Asset or LedgerAccountType.Expense ? LedgerAccountNature.Debit : LedgerAccountNature.Credit,
        ReferentialCode = analytic ? referential : null,
    };

    private static ManagementViewLine Line(int order, string code, string? parent, ManagementViewLineKind kind, string? from = null,
        string? to = null, ManagementViewLineSign sign = ManagementViewLineSign.Plus) => new()
    {
        Order = order, Code = code, Description = "Linha " + code, ParentCode = parent, Kind = kind, AccountFrom = from, AccountTo = to,
        Sign = sign,
    };

    /// <summary>
    /// Empresa 12.345.678 (matriz 01, filial 02). Saldo inicial em 2025; venda (centro CC01), despesa em linhas
    /// simples com continuação de histórico e apuração de resultado em 31/12/2026.
    /// </summary>
    internal static async Task<IUnitOfWork> SeedAsync(bool closing = true, Action<IUnitOfWork>? customize = null)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Municipalities.Add(new Municipality { Code = "3550308", Name = "São Paulo", StateAbbreviation = "SP" });
        db.Context.Branchs.AddRange(
            new Branch { Code = "01", BranchName = "MATRIZ", LegalName = "AGRO TESTE LTDA", TaxId = "12.345.678/0001-95",
                StateCode = "SP", MunicipalityCode = "3550308", StateRegistration = "110.042.490.114" },
            new Branch { Code = "02", BranchName = "FILIAL", TaxId = "12.345.678/0002-76", StateCode = "SP", MunicipalityCode = "3550308" });
        db.Context.EcdSettings.Add(new EcdSettings
        {
            BranchCode = "01", BookNumber = 7, HasNire = true, Nire = "35212345678", ConstitutiveActsDate = new DateTime(2010, 5, 3),
            ReferentialPlanCode = "1", SignerName = "JOÃO DIRETOR", SignerCpf = "52998224725", SignerQualificationCode = "205",
            SignerEmail = "joao@agro.com", SignerPhone = "11999990000",
            AccountantCrcSequence = "SP/2026/000123", AccountantCrcValidUntil = new DateTime(2026, 12, 31),
        });
        db.Context.SpedFiscalSettings.Add(new SpedFiscalSettings
        {
            BranchCode = "01", AccountantName = "MARIA CONTADORA", AccountantCpf = "11144477735", AccountantCrc = "SP123456O8",
            AccountantEmail = "maria@contab.com", AccountantPhone = "1133334444",
        });
        db.Context.AccountingSettings.Add(new AccountingSettings { CutoverDate = new DateTime(2025, 1, 1) });
        db.Context.LedgerAccounts.AddRange(
            Account("1", "ATIVO", LedgerAccountType.Asset, null, analytic: false),
            Account("1.1.01", "Caixa", LedgerAccountType.Asset, "1"),
            Account("2", "PASSIVO E PL", LedgerAccountType.Equity, null, analytic: false),
            Account("2.3.01", "Capital", LedgerAccountType.Equity, "2"),
            Account("2.3.02", "Lucros acumulados", LedgerAccountType.Equity, "2"),
            Account("3", "RECEITAS", LedgerAccountType.Revenue, null, analytic: false),
            Account("3.1.01", "Vendas", LedgerAccountType.Revenue, "3"),
            Account("4", "DESPESAS", LedgerAccountType.Expense, null, analytic: false),
            Account("4.1.01", "Despesa", LedgerAccountType.Expense, "4"),
            // Sem código referencial e sem movimento: não bloqueia e não entra no arquivo
            Account("4.1.99", "Sem uso", LedgerAccountType.Expense, "4", referential: null));
        db.Context.CostCenters.AddRange(new CostCenter { Code = "CC01", Name = "Lavoura" }, new CostCenter { Code = "CC02", Name = "Sem uso" });
        db.Context.ManagementViews.AddRange(
            new ManagementView
            {
                Code = "BP", Name = "Balanço", Type = ManagementViewType.BalanceSheet,
                Lines =
                [
                    Line(1, "A", null, ManagementViewLineKind.Total),
                    Line(2, "A1", "A", ManagementViewLineKind.Accounts, "1", "1.9"),
                    Line(3, "P", null, ManagementViewLineKind.Total),
                    Line(4, "P1", "P", ManagementViewLineKind.Accounts, "2", "2.9"),
                ],
            },
            new ManagementView
            {
                Code = "DRE", Name = "DRE", Type = ManagementViewType.IncomeStatement,
                Lines =
                [
                    Line(1, "LL", null, ManagementViewLineKind.Total),
                    Line(2, "R", "LL", ManagementViewLineKind.Accounts, "3", "3.9"),
                    Line(3, "D", "LL", ManagementViewLineKind.Accounts, "4", "4.9", ManagementViewLineSign.Minus),
                ],
            });
        for (var m = 1; m <= 12; m++)
            db.Context.AccountingPeriods.Add(new AccountingPeriod
            {
                Year = 2026, Month = m, StartDate = new DateTime(2026, m, 1), EndDate = new DateTime(2026, m, 1).AddMonths(1).AddDays(-1),
                Status = AccountingPeriodStatus.Closed,
            });

        db.Context.JournalEntries.AddRange(
            Entry(new DateTime(2025, 1, 1), Dbl("1.1.01", "2.3.01", 1000m)),
            Entry(new DateTime(2026, 3, 10), Dbl("1.1.01", "3.1.01", 500m, ccc: "CC01", h: "Venda à vista")),
            Entry(new DateTime(2026, 5, 5), Deb("4.1.01", 200m, "CC01", "Frete "), Cont("da safra"), Cre("1.1.01", 200m, "Pagamento")));
        if (closing)
            db.Context.JournalEntries.Add(Entry(new DateTime(2026, 12, 31), "ResultClosing",
                Dbl("3.1.01", "2.3.02", 500m, ccd: "CC01"), Dbl("2.3.02", "4.1.01", 200m, ccc: "CC01")));
        customize?.Invoke(db);
        await db.SaveChangesAsync();
        return db;
    }

    internal static EcdRequest Request() => new() { BranchCode = "02", Year = 2026 };

    internal static EcdFileService Service(IUnitOfWork db) => new(new EcdDataLoader(db));

    private static async Task<List<string[]>> GenerateAsync(IUnitOfWork db)
    {
        var (content, _) = await Service(db).GenerateAsync(Request());
        var text = Encoding.Latin1.GetString(content);
        Assert.EndsWith("\r\n", text);
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l[1..^1].Split('|')).ToList();
    }

    private static List<string[]> Of(List<string[]> lines, string reg) => lines.Where(l => l[0] == reg).ToList();

    private static decimal Dec(string s) => decimal.Parse(s, Pt);

    // ---- geração ----

    [Fact]
    public async Task Generate_HeaderAndTermsAndBlock9Counts()
    {
        var db = await SeedAsync();
        var (content, fileName) = await Service(db).GenerateAsync(Request());
        var lines = Encoding.Latin1.GetString(content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l[1..^1].Split('|')).ToList();

        Assert.Equal("ECD_12345678000195_2026.txt", fileName);
        Assert.Equal(["0000", "LECD", "01012026", "31122026", "AGRO TESTE LTDA", "12345678000195", "SP", "110042490114", "3550308", "",
            "", "0", "1", "0", "", "0", "0", "", "N", "N", "0", "0", "1"], lines[0]);
        Assert.Equal(["I010", "G", "9.00"], Of(lines, "I010").Single());

        var total = lines.Count.ToString();
        Assert.Equal(["I030", "TERMO DE ABERTURA", "7", "DIARIO GERAL", total, "AGRO TESTE LTDA", "35212345678", "12345678000195",
            "03052010", "", "São Paulo", "31122026"], Of(lines, "I030").Single());
        Assert.Equal(["J900", "TERMO DE ENCERRAMENTO", "7", "DIARIO GERAL", "AGRO TESTE LTDA", total, "01012026", "31122026"],
            Of(lines, "J900").Single());
        Assert.Equal(total, lines[^1][1]);

        var signers = Of(lines, "J930");
        Assert.Equal(["J930", "JOÃO DIRETOR", "52998224725", "Administrador", "205", "", "joao@agro.com", "11999990000", "", "", "", "S"], signers[0]);
        Assert.Equal(["J930", "MARIA CONTADORA", "11144477735", "Contador", "900", "SP123456O8", "maria@contab.com", "1133334444", "SP", "SP/2026/000123", "31122026", "N"],
            signers[1]);

        // 9900 confere com a contagem real; X990 = linhas do bloco
        foreach (var r in Of(lines, "9900"))
            Assert.Equal(lines.Count(l => l[0] == r[1]), int.Parse(r[2]));
        foreach (var block in new[] { '0', 'I', 'J', 'K' })
            Assert.Equal(lines.Count(l => l[0][0] == block), int.Parse(Of(lines, $"{block}990").Single()[1]));
        Assert.Equal(["K001", "1"], Of(lines, "K001").Single());
    }

    [Fact]
    public async Task Generate_PlanOnlyUsedAccountsWithParentsAndViewLines()
    {
        var lines = await GenerateAsync(await SeedAsync());

        var plan = Of(lines, "I050");
        Assert.Equal(["1", "1.1.01", "2", "2.3.01", "2.3.02", "3", "3.1.01", "4", "4.1.01"], plan.Select(l => l[5]));
        Assert.Equal(["I050", "01012025", "01", "A", "2", "1.1.01", "1", "Caixa"], plan[1]);
        Assert.Equal(["I050", "01012025", "04", "S", "1", "3", "", "RECEITAS"], plan[5]);
        Assert.Equal("03", plan[3][2]);

        // Filhos da analítica Caixa: I051 e I052 logo depois dela
        var i = lines.FindIndex(l => l[0] == "I050" && l[5] == "1.1.01");
        Assert.Equal(["I051", "", "REF"], lines[i + 1]);
        Assert.Equal(["I052", "", "A1"], lines[i + 2]);
        Assert.Contains(lines, l => l[0] == "I052" && l[2] == "R");
    }

    [Fact]
    public async Task Generate_CostCenterSplitsI155AndI100OnlyUsed()
    {
        var lines = await GenerateAsync(await SeedAsync());

        Assert.Equal(["I100", "01012025", "CC01", "Lavoura"], Of(lines, "I100").Single());
        var march = MonthRows(lines, 3);
        Assert.Equal(["I155", "3.1.01", "CC01", "0,00", "C", "0,00", "500,00", "500,00", "C"], march.Single(l => l[1] == "3.1.01"));
    }

    [Fact]
    public async Task Generate_JanuaryOpeningComesFromPreviousYear()
    {
        var january = MonthRows(await GenerateAsync(await SeedAsync()), 1);

        Assert.Equal(["I155", "1.1.01", "", "1000,00", "D", "0,00", "0,00", "1000,00", "D"], january.Single(l => l[1] == "1.1.01"));
        Assert.Equal(["I155", "2.3.01", "", "1000,00", "C", "0,00", "0,00", "1000,00", "C"], january.Single(l => l[1] == "2.3.01"));
        Assert.Equal(2, january.Count);
    }

    private static List<string[]> MonthRows(List<string[]> lines, int month)
    {
        var start = lines.FindIndex(l => l[0] == "I150" && l[1] == $"01{month:00}2026");
        return lines.Skip(start + 1).TakeWhile(l => l[0] == "I155").ToList();
    }

    [Fact]
    public async Task Generate_I155MatchesTrialBalance()
    {
        var db = await SeedAsync();
        var lines = await GenerateAsync(db);
        var trial = new TrialBalanceReportService(db, null!);

        for (var month = 1; month <= 12; month++)
        {
            var from = new DateTime(2026, month, 1);
            var rows = (await trial.RowsAsync(new TrialBalanceReportRequest { DateFrom = from, DateTo = from.AddMonths(1).AddDays(-1) }))
                .Where(r => r.Analytic).ToList();
            var file = MonthRows(lines, month).GroupBy(l => l[1]).ToDictionary(g => g.Key, g => (
                Opening: g.Sum(l => Dec(l[3]) * (l[4] == "D" ? 1 : -1)), Debit: g.Sum(l => Dec(l[5])), Credit: g.Sum(l => Dec(l[6]))));

            Assert.Equal(rows.Select(r => r.AccountCode).Order(), file.Keys.Order());
            foreach (var r in rows)
            {
                Assert.Equal(r.Opening * (r.OpeningSide == "C" ? -1 : 1), file[r.AccountCode].Opening);
                Assert.Equal(r.Debit, file[r.AccountCode].Debit);
                Assert.Equal(r.Credit, file[r.AccountCode].Credit);
            }
        }
    }

    [Fact]
    public async Task Generate_EntriesBecomeI200AndI250()
    {
        var lines = await GenerateAsync(await SeedAsync());

        var entries = Of(lines, "I200");
        Assert.Equal(3, entries.Count);
        var sale = lines.FindIndex(l => l[0] == "I200" && l[2] == "10032026");
        Assert.Equal("500,00", lines[sale][3]);
        Assert.Equal("N", lines[sale][4]);
        // Partida dobrada: duas linhas I250
        Assert.Equal(["I250", "1.1.01", "", "500,00", "D", "", "", "Venda à vista", ""], lines[sale + 1]);
        Assert.Equal(["I250", "3.1.01", "CC01", "500,00", "C", "", "", "Venda à vista", ""], lines[sale + 2]);

        // Linhas simples e continuação do histórico
        var expense = lines.FindIndex(l => l[0] == "I200" && l[2] == "05052026");
        Assert.Equal(["I250", "4.1.01", "CC01", "200,00", "D", "", "", "Frete da safra", ""], lines[expense + 1]);
        Assert.Equal(["I250", "1.1.01", "", "200,00", "C", "", "", "Pagamento", ""], lines[expense + 2]);
        Assert.Equal("I200", lines[expense + 3][0]);
    }

    [Fact]
    public async Task Generate_ResultClosingIsEncerramentoWithI355()
    {
        var lines = await GenerateAsync(await SeedAsync());

        var closing = Of(lines, "I200").Single(l => l[2] == "31122026");
        Assert.Equal("E", closing[4]);
        Assert.Equal("700,00", closing[3]);
        Assert.Equal(["I350", "31122026"], Of(lines, "I350").Single());
        Assert.Equal([["I355", "3.1.01", "CC01", "500,00", "C"], ["I355", "4.1.01", "CC01", "200,00", "D"]], Of(lines, "I355"));
    }

    [Fact]
    public async Task Generate_J100AndJ150MatchSiagroStatements()
    {
        var db = await SeedAsync();
        var lines = await GenerateAsync(db);

        var bp = await new BalanceSheetReportService(db, null!).RowsAsync(new BalanceSheetReportRequest
            { Date = new DateTime(2026, 12, 31), CompareDate = new DateTime(2025, 12, 31), ViewCode = "BP" });
        var j100 = Of(lines, "J100");
        Assert.Equal(bp.Select(r => r.Code), j100.Select(l => l[1]));
        foreach (var r in bp)
        {
            var l = j100.Single(x => x[1] == r.Code);
            Assert.Equal(r.Balance, Dec(l[9]));
            Assert.Equal(r.CompareBalance, Dec(l[7]));
        }

        Assert.Equal(["J100", "A", "T", "1", "", "A", "Linha A", "1000,00", "D", "1300,00", "D", ""], j100[0]);
        Assert.Equal(["J100", "P1", "D", "2", "P", "P", "Linha P1", "1000,00", "C", "1300,00", "C", ""], j100[3]);

        var dre = await new IncomeStatementReportService(db, null!).RowsAsync(new IncomeStatementReportRequest
            { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 12, 31), ViewCode = "DRE" });
        var j150 = Of(lines, "J150");
        Assert.Equal(dre.Select(r => r.Code), j150.Select(l => l[2]));
        foreach (var r in dre) Assert.Equal(r.Period, Dec(j150.Single(x => x[2] == r.Code)[9]));
        Assert.Equal(["J150", "1", "LL", "T", "1", "", "Linha LL", "0,00", "C", "300,00", "C", "R", ""], j150[0]);
        Assert.Equal(["J150", "3", "D", "D", "2", "LL", "Linha D", "0,00", "D", "200,00", "D", "D", ""], j150[2]);
    }

    [Fact]
    public async Task Generate_SubstituteCarriesReplacedHash()
    {
        var db = await SeedAsync();
        var request = Request();
        request.Purpose = 1;
        request.ReplacedBookHash = "ABC123";

        var (content, _) = await Service(db).GenerateAsync(request);
        var header = Encoding.Latin1.GetString(content).Split("\r\n")[0][1..^1].Split('|');

        Assert.Equal("1", header[13]);
        Assert.Equal("ABC123", header[14]);
    }

    [Fact]
    public async Task Check_ReturnsTotals()
    {
        var check = await Service(await SeedAsync()).CheckAsync(Request());

        Assert.Empty(check.Issues);
        Assert.Equal(1400m, check.Totals.TotalDebit);
        Assert.Equal(1400m, check.Totals.TotalCredit);
        Assert.Equal(300m, check.Totals.NetIncome);
    }

    [Fact]
    public async Task Generate_BlockingIssuesThrowWithAllMessages()
    {
        var db = await SeedAsync(customize: d => d.Context.AccountingPeriods.RemoveRange(
            d.Context.AccountingPeriods.Local.Where(p => p.Month is 7 or 8).ToList()));

        var e = await Assert.ThrowsAsync<BusinessException>(() => Service(db).GenerateAsync(Request()));

        Assert.Contains("Mês 07/2026 não fechado.", e.Message);
        Assert.Contains("Mês 08/2026 não fechado.", e.Message);
    }
}
