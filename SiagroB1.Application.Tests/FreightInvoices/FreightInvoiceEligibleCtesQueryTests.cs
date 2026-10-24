using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FreightInvoices;

/// <summary>CT-e elegíveis para a fatura de frete.</summary>
public class FreightInvoiceEligibleCtesQueryTests
{
    private const string Branch = "01";
    private static readonly DateTime Issue = new(2026, 10, 22, 10, 0, 0);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FreightInvoiceEligibleCtesQuery Query() => new(_db);

    private async Task<TransportDocument> CteAsync(string taker = "DES", CteStatus status = CteStatus.Authorized,
        bool withTitle = true, params (decimal net, decimal settled)[] installments)
    {
        var cte = new TransportDocument
        {
            BranchCode = Branch, Series = 1, Number = 100, AccessKey = new string('1', 44), Status = status, IssuedAt = Issue,
            TakerRole = 3, StartMunicipalityCode = "4106902", StartState = "PR", EndMunicipalityCode = "3550308", EndState = "SP",
            Parties =
            [
                new TransportDocumentParty { Role = CtePartyRole.Sender, CardCode = "REM", Name = "REM" },
                new TransportDocumentParty { Role = CtePartyRole.Recipient, CardCode = taker, Name = "DES" },
            ],
            Quantities = [new TransportDocumentQuantity { MeasureType = "PESO BRUTO", UnitCode = "02", Quantity = 30m }],
        };
        _db.Context.TransportDocuments.Add(cte);
        if (withTitle)
        {
            if (installments.Length == 0) installments = [(1000m, 0m)];
            var n = 1;
            foreach (var (net, settled) in installments)
                _db.Context.FinancialDocuments.Add(new FinancialDocument
                {
                    Code = Guid.NewGuid().ToString("N")[..12], DocNumberKey = Guid.NewGuid(), BranchCode = Branch,
                    Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Firm,
                    Status = FinancialDocumentStatus.Open, CardCode = taker, DocumentDate = Issue, DueDate = Issue.AddDays(30),
                    NetAmount = net, SettledAmount = settled, InstallmentNumber = n++,
                    OriginType = FinancialDocumentOrigin.TransportDocument, OriginKey = cte.Key,
                    CreatedBy = "t", UpdatedBy = "t",
                });
        }
        await _db.SaveChangesAsync();
        return cte;
    }

    private async Task<FreightInvoice> InvoiceAsync(TransportDocument cte, FreightInvoiceStatus status, bool active)
    {
        var invoice = new FreightInvoice
        {
            BranchCode = Branch, Code = Guid.NewGuid().ToString("N")[..10], DocNumberKey = Guid.NewGuid(), TakerCardCode = "DES",
            Status = status, CreatedBy = "t",
            Items = [new FreightInvoiceItem { TransportDocumentKey = cte.Key, LineNum = 1, Amount = 1000m, IsActive = active }],
        };
        _db.Context.FreightInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    private Task<IReadOnlyList<FreightInvoiceEligibleCte>> ListAsync(Guid? ignore = null) =>
        Query().ListAsync(Branch, "DES", null, null, ignore);

    [Fact]
    public async Task Authorized_WithOpenTitle_IsEligible()
    {
        var cte = await CteAsync();
        var row = Assert.Single(await ListAsync());
        Assert.Equal(cte.Key, row.TransportDocumentKey);
        Assert.Equal(1000m, row.Amount);
        Assert.Equal(30000m, row.WeightKg);
        Assert.Equal(100, row.Number);
    }

    [Fact]
    public async Task OtherTaker_Excluded()
    {
        await CteAsync(taker: "OUTRO");
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Cancelled_Excluded()
    {
        await CteAsync(status: CteStatus.Cancelled);
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task AlreadyInActiveInvoice_Excluded()
    {
        var cte = await CteAsync();
        await InvoiceAsync(cte, FreightInvoiceStatus.Issued, active: true);
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task IgnoredInvoice_ItemsDoNotCount()
    {
        var cte = await CteAsync();
        var invoice = await InvoiceAsync(cte, FreightInvoiceStatus.Draft, active: true);
        Assert.Single(await ListAsync(invoice.Key));
    }

    [Fact]
    public async Task InCancelledInvoice_IsEligibleAgain()
    {
        var cte = await CteAsync();
        await InvoiceAsync(cte, FreightInvoiceStatus.Cancelled, active: false);
        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task UsageWithoutFinancial_Excluded()
    {
        await CteAsync(withTitle: false);
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task OneInstallmentSettled_Excluded()
    {
        await CteAsync(installments: [(500m, 500m), (500m, 0m)]);
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Amount_SumsAllInstallments()
    {
        await CteAsync(installments: [(500m, 0m), (300m, 0m)]);
        Assert.Equal(800m, Assert.Single(await ListAsync()).Amount);
    }
}
