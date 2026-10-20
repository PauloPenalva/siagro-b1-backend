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
}
