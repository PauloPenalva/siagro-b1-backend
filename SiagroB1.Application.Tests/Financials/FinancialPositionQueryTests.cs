using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialPositionQueryTests
{
    private static readonly DateTime Base = new(2026, 10, 10);

    private static FinancialDocument Doc(string cardCode, FinancialDirection direction,
        FinancialDocumentNature nature = FinancialDocumentNature.Firm, decimal amount = 100m) => new()
    {
        Key = Guid.NewGuid(), Code = "FN" + Guid.NewGuid().ToString("N")[..6], CardCode = cardCode,
        Nature = nature, OriginType = nature == FinancialDocumentNature.Firm ? FinancialDocumentOrigin.PurchaseFiscalReconciliation : FinancialDocumentOrigin.Manual, DocumentDate = new DateTime(2026, 10, 1),
        DueDate = new DateTime(2026, 10, 30), NetAmount = amount, Direction = direction
    };

    private static FinancialSettlement Offset(FinancialDocument firm, FinancialDocument provisional, decimal amount) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = firm.Key, Amount = amount, SettlementDate = new DateTime(2026, 10, 2),
        Origin = FinancialSettlementOrigin.InvoiceOffset, CompositionKey = Guid.NewGuid(), RelatedDocumentKey = provisional.Key
    };

    [Fact]
    public async Task Loads_only_direction_and_partner()
    {
        var db = TestDb.CreateUnitOfWork();
        var mine = Doc("C1", FinancialDirection.Receivable);
        db.Context.FinancialDocuments.AddRange(mine, Doc("C1", FinancialDirection.Payable), Doc("C2", FinancialDirection.Receivable));
        await db.Context.SaveChangesAsync();

        var position = await FinancialPositionQuery.LoadAsync(db.Context,
            new FinancialPositionFilter(FinancialDirection.Receivable, "C1", Base), FinancialPositionMode.Firm, CancellationToken.None);

        Assert.Equal(mine.Key, Assert.Single(position.Titles).Document.Key);
    }

    [Fact]
    public async Task Brings_provisional_of_composed_firms_even_when_filtered_by_partner()
    {
        var db = TestDb.CreateUnitOfWork();
        var provisional = Doc("C9", FinancialDirection.Payable, FinancialDocumentNature.Provisional, 300m);
        var firm = Doc("C1", FinancialDirection.Payable);
        var otherFirm = Doc("C3", FinancialDirection.Payable);
        db.Context.FinancialDocuments.AddRange(provisional, firm, otherFirm);
        db.Context.FinancialSettlements.AddRange(
            Offset(firm, provisional, 100m), Offset(otherFirm, provisional, 100m),
            new FinancialSettlement
            {
                Key = Guid.NewGuid(), FinancialDocumentKey = provisional.Key, Amount = 150m,
                SettlementDate = new DateTime(2026, 10, 5), Origin = FinancialSettlementOrigin.Manual
            });
        await db.Context.SaveChangesAsync();

        var position = await FinancialPositionQuery.LoadAsync(db.Context,
            new FinancialPositionFilter(FinancialDirection.Payable, "C1", Base), FinancialPositionMode.Firm, CancellationToken.None);

        // 150 distribuídos por vencimento/código: o grupo inteiro precisa estar no cálculo.
        var byKey = position.Titles.ToDictionary(t => t.Document.Key);
        Assert.Equal(150m, byKey[firm.Key].SettledAtBase + byKey[otherFirm.Key].SettledAtBase);

        var provisionalView = await FinancialPositionQuery.LoadAsync(db.Context,
            new FinancialPositionFilter(FinancialDirection.Payable, "C1", Base), FinancialPositionMode.Provisional, CancellationToken.None);
        var title = Assert.Single(provisionalView.Titles);
        Assert.Equal(provisional.Key, title.Document.Key);
        Assert.Equal(200m, title.Amount);
        Assert.Equal(50m, title.OpenAtBase);
    }

    [Fact]
    public async Task Out_of_period_firm_composed_in_same_provisional_is_still_loaded()
    {
        var db = TestDb.CreateUnitOfWork();
        var provisional = Doc("C1", FinancialDirection.Payable, FinancialDocumentNature.Provisional, 200m);
        var inPeriod = Doc("C1", FinancialDirection.Payable);
        inPeriod.DueDate = new DateTime(2026, 10, 20);
        var outOfPeriod = Doc("C1", FinancialDirection.Payable);
        outOfPeriod.DueDate = new DateTime(2026, 10, 5);
        db.Context.FinancialDocuments.AddRange(provisional, inPeriod, outOfPeriod);
        db.Context.FinancialSettlements.AddRange(
            Offset(inPeriod, provisional, 100m), Offset(outOfPeriod, provisional, 100m),
            new FinancialSettlement
            {
                Key = Guid.NewGuid(), FinancialDocumentKey = provisional.Key, Amount = 100m,
                SettlementDate = new DateTime(2026, 10, 6), Origin = FinancialSettlementOrigin.Manual
            });
        await db.Context.SaveChangesAsync();

        var position = await FinancialPositionQuery.LoadAsync(db.Context,
            new FinancialPositionFilter(FinancialDirection.Payable, "C1", new DateTime(2026, 10, 31),
                new DateTime(2026, 10, 15), new DateTime(2026, 10, 31)),
            FinancialPositionMode.Firm, CancellationToken.None);

        // O firme de vencimento anterior (fora do período) absorve o pagamento primeiro.
        var byKey = position.Titles.ToDictionary(t => t.Document.Key);
        Assert.Equal(100m, byKey[outOfPeriod.Key].SettledAtBase);
        Assert.Equal(0m, byKey[inPeriod.Key].SettledAtBase);
        Assert.Equal(100m, byKey[inPeriod.Key].OpenAtBase);
    }

    [Fact]
    public async Task Loads_more_than_2100_titles()
    {
        // Mais títulos que o limite de 2100 parâmetros do SQL Server (o InMemory só prova o fluxo; o SQL vai no teste abaixo).
        var db = TestDb.CreateUnitOfWork();
        var docs = Enumerable.Range(0, 2500).Select(_ => Doc("C1", FinancialDirection.Payable)).ToList();
        db.Context.FinancialDocuments.AddRange(docs);
        await db.Context.SaveChangesAsync();

        var position = await FinancialPositionQuery.LoadAsync(db.Context,
            new FinancialPositionFilter(FinancialDirection.Payable, "C1", Base), FinancialPositionMode.Firm, CancellationToken.None);

        Assert.Equal(2500, position.Titles.Count);
    }

    [Fact]
    public void EF_Parameter_contains_translates_to_a_single_openjson_parameter_on_sql_server()
    {
        // EF Core 10 traduz Contains de lista como @p1, @p2... por padrão; EF.Parameter força um único parâmetro JSON.
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SiagroB1.Infra.Context.AppDbContext>()
            .UseSqlServer("Server=.;Database=none;Trusted_Connection=True").Options;
        using var ctx = new SiagroB1.Infra.Context.AppDbContext(options);
        var keys = Enumerable.Range(0, 3000).Select(_ => Guid.NewGuid()).ToList();

        var sql = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToQueryString(
            ctx.FinancialDocuments.Where(d => Microsoft.EntityFrameworkCore.EF.Parameter(keys).Contains(d.Key)));

        Assert.Contains("OPENJSON(@keys)", sql);
        Assert.DoesNotContain("@keys1", sql);
    }
}
