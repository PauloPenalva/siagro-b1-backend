using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Visão gerencial (CTS): cadastro das linhas de DRE/Balanço com Total, Contas e Fórmula.</summary>
public class ManagementViewsServiceTests
{
    private static ManagementViewLine Line(string code, ManagementViewLineKind kind, string? parent = null,
        string? from = null, string? to = null, string? formula = null, int order = 0) => new()
    {
        Code = code, Description = code, Kind = kind, ParentCode = parent, AccountFrom = from, AccountTo = to,
        Formula = formula, Order = order, Sign = ManagementViewLineSign.Plus,
    };

    private static ManagementView View(params ManagementViewLine[] lines) => new()
    {
        Code = "DRE2", Name = "DRE teste", Type = ManagementViewType.IncomeStatement, Lines = lines.ToList(),
    };

    [Fact]
    public async Task Save_replaces_lines()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new ManagementViewsService(db);
        await service.SaveAsync(View(Line("01", ManagementViewLineKind.Accounts, from: "3", to: "3.9")), "u");

        await service.SaveAsync(View(Line("A", ManagementViewLineKind.Accounts, from: "4", to: "4.9"),
            Line("B", ManagementViewLineKind.Formula, formula: "A")), "u");

        var saved = await db.Context.ManagementViews.AsNoTracking().Include(v => v.Lines).SingleAsync(v => v.Code == "DRE2");
        Assert.Equal(["A", "B"], saved.Lines.Select(l => l.Code).Order());
        Assert.Equal("u", saved.UpdatedBy);
    }

    [Fact] // [RF 4]
    public async Task Rejects_formula_with_unknown_line()
    {
        var service = new ManagementViewsService(TestDb.CreateUnitOfWork());
        var e = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(View(
            Line("01", ManagementViewLineKind.Accounts, from: "3", to: "3"),
            Line("02", ManagementViewLineKind.Formula, formula: "01 - 99")), "u"));
        Assert.Equal("Linha 02: a fórmula cita a linha 99, que não existe na visão.", e.Message);
    }

    [Fact] // [RF 4]
    public async Task Rejects_formula_cycle()
    {
        var service = new ManagementViewsService(TestDb.CreateUnitOfWork());
        var e = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(View(
            Line("01", ManagementViewLineKind.Formula, formula: "02"),
            Line("02", ManagementViewLineKind.Formula, formula: "01")), "u"));
        Assert.StartsWith("Fórmula circular", e.Message);
    }

    [Fact]
    public async Task Rejects_bad_formula_syntax()
    {
        var service = new ManagementViewsService(TestDb.CreateUnitOfWork());
        var e = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(View(
            Line("01", ManagementViewLineKind.Accounts, from: "3", to: "3"),
            Line("02", ManagementViewLineKind.Formula, formula: "01 * 2")), "u"));
        Assert.Equal("Linha 02: fórmula inválida. Use códigos de linha somados ou subtraídos, como 01 - 02 + 05.", e.Message);
    }

    [Fact]
    public async Task Rejects_parent_that_is_not_total()
    {
        var service = new ManagementViewsService(TestDb.CreateUnitOfWork());
        var e = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(View(
            Line("01", ManagementViewLineKind.Accounts, from: "3", to: "3"),
            Line("02", ManagementViewLineKind.Accounts, parent: "01", from: "4", to: "4")), "u"));
        Assert.Equal("Linha 02: a linha superior 01 precisa ser do tipo Total.", e.Message);
    }

    [Fact]
    public async Task Accounts_line_requires_both_ends_or_none()
    {
        var service = new ManagementViewsService(TestDb.CreateUnitOfWork());
        await service.SaveAsync(View(Line("01", ManagementViewLineKind.Accounts)), "u"); // faixa vazia = zero
        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            service.SaveAsync(View(Line("01", ManagementViewLineKind.Accounts, from: "3")), "u"));
        Assert.Equal("Linha 01: informe a conta inicial e a final.", e.Message);
    }

    [Fact]
    public void Formula_parse_reads_signs()
    {
        var parsed = ManagementViewFormula.Parse("01 - 02 + 05");
        Assert.Equal([(1, "01"), (-1, "02"), (1, "05")], parsed);
    }

    [Fact]
    public async Task Seed_has_income_statement_and_balance_sheet()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new ManagementViewsService(db);
        await service.SaveAsync(ManagementViewSeed.IncomeStatement(), "u");
        await service.SaveAsync(ManagementViewSeed.BalanceSheet(), "u");

        var views = await db.Context.ManagementViews.AsNoTracking().Include(v => v.Lines).ToListAsync();
        Assert.Equal(10, views.Single(v => v.Code == "DRE").Lines.Count);
        Assert.Equal(7, views.Single(v => v.Code == "BP").Lines.Count);
    }

    [Fact]
    public async Task Rejects_duplicate_line_code()
    {
        var service = new ManagementViewsService(TestDb.CreateUnitOfWork());
        var e = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(View(
            Line("01", ManagementViewLineKind.Accounts, from: "3", to: "3"),
            Line("01", ManagementViewLineKind.Accounts, from: "4", to: "4")), "u"));
        Assert.Equal("Código de linha 01 repetido.", e.Message);
    }
}
