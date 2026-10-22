using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Accounting.Postings;

public class StandardPostingsServiceTests
{
    private static async Task<IUnitOfWork> Db()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1.03", Name = "Estoque", Type = LedgerAccountType.Asset },
            new LedgerAccount { Code = "2.1.01", Name = "Fornecedores", Type = LedgerAccountType.Liability },
            new LedgerAccount { Code = "2", Name = "Passivo", Type = LedgerAccountType.Liability, AllowsPosting = false });
        await db.SaveChangesAsync();
        return db;
    }

    private static StandardPostingLine Line(string seq = "001", string value = "Line.FiscalStockCost",
        string? debitSource = "Item.LedgerAccount", string? creditSource = "Fixed", string? creditCode = "2.1.01",
        JournalEntryLineType type = JournalEntryLineType.Double) => new()
    {
        Sequence = seq, EntryType = type, DebitAccountSource = debitSource, CreditAccountSource = creditSource,
        CreditAccountCode = creditCode, ValueSource = value, HistoryTemplate = "NF {DocNumber}", Active = true,
    };

    private static StandardPosting Posting(string code, params StandardPostingLine[] lines) =>
        new() { Code = code, Active = true, Lines = lines.ToList() };

    private static async Task<string> Reject(IUnitOfWork db, StandardPosting p) =>
        (await Assert.ThrowsAsync<DefaultException>(() => new StandardPostingsService(db).SaveAsync(p, "admin"))).Message;

    [Fact]
    public async Task Rejects_unknown_code() =>
        Assert.Equal("Código de lançamento padrão 999 não existe.", await Reject(await Db(), Posting("999", Line())));

    [Fact]
    public async Task Rejects_value_source_from_other_context() =>
        Assert.Equal("Seq. 001: fonte de valor Settlement.Amount não se aplica ao código 650.",
            await Reject(await Db(), Posting("650", Line(value: "Settlement.Amount"))));

    [Fact]
    public async Task Rejects_account_source_from_other_context() =>
        Assert.Equal("Seq. 001: fonte de conta FinancialAccount.LedgerAccount não se aplica ao código 650.",
            await Reject(await Db(), Posting("650", Line(debitSource: "FinancialAccount.LedgerAccount"))));

    [Fact]
    public async Task Fixed_requires_analytic_account()
    {
        var db = await Db();
        Assert.Equal("Seq. 001: a conta fixa de crédito 2 não existe ou é sintética.",
            await Reject(db, Posting("650", Line(creditCode: "2"))));
        Assert.Equal("Seq. 001: informe a conta fixa de crédito.",
            await Reject(db, Posting("650", Line(creditCode: null))));
    }

    [Fact]
    public async Task Double_requires_both_sides() =>
        Assert.Equal("Seq. 001: informe a origem das contas de débito e de crédito.",
            await Reject(await Db(), Posting("650", Line(creditSource: null))));

    [Fact]
    public async Task Debit_only_line_rejects_credit_side() =>
        Assert.Equal("Seq. 001: linha de débito não tem conta de crédito.",
            await Reject(await Db(), Posting("650", Line(type: JournalEntryLineType.Debit))));

    [Fact]
    public async Task Rejects_duplicate_sequence() =>
        Assert.Equal("Sequência 001 repetida.", await Reject(await Db(), Posting("650", Line(), Line())));

    [Fact]
    public async Task Fixed_dimension_requires_code()
    {
        var line = Line();
        line.DebitCostCenterSource = DimensionSource.Fixed;
        Assert.Equal("Seq. 001: informe o centro de custo fixo de débito.", await Reject(await Db(), Posting("650", line)));
    }

    [Fact]
    public async Task Save_replaces_lines()
    {
        var db = await Db();
        await new StandardPostingsService(db).SaveAsync(Posting("650", Line("001"), Line("002")), "admin");
        await new StandardPostingsService(db).SaveAsync(Posting("650", Line("003")), "joao");

        var saved = await db.Context.StandardPostings.Include(p => p.Lines).SingleAsync();
        Assert.Equal(["003"], saved.Lines.Select(l => l.Sequence));
        Assert.Equal("joao", saved.UpdatedBy);
        Assert.Equal(1, await db.Context.StandardPostingLines.CountAsync());
    }
}
