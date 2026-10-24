using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FreightInvoices;

public class FreightInvoicesBatchCreateServiceTests
{
    private const string Branch = "01";
    private static readonly DateTime Issue = new(2026, 10, 22, 10, 0, 0);
    private static readonly DateTime From = new(2026, 10, 1);
    private static readonly DateTime To = new(2026, 10, 31);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FreightInvoicesBatchCreateService Svc(string erp = "STANDALONE")
    {
        var cfg = TestConfiguration.Erp(erp);
        var q = new FreightInvoiceEligibleCtesQuery(_db);
        var seq = new FakeDocNumberSequenceService();
        var save = new FreightInvoicesSaveService(_db, seq,
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["T1"] = "Um", ["T2"] = "Dois" }), q, cfg);
        var issue = new FreightInvoicesIssueService(_db, seq, q, cfg);
        return new FreightInvoicesBatchCreateService(_db, q, save, issue, cfg);
    }

    private async Task CteAsync(int number, string taker)
    {
        var cte = new TransportDocument
        {
            BranchCode = Branch, Series = 1, Number = number, AccessKey = new string('1', 44), Status = CteStatus.Authorized,
            IssuedAt = Issue, TakerRole = 3,
            Parties = [new TransportDocumentParty { Role = CtePartyRole.Recipient, CardCode = taker, Name = taker }],
        };
        _db.Context.TransportDocuments.Add(cte);
        _db.Context.FinancialDocuments.Add(new FinancialDocument
        {
            Code = "CTE-" + number, DocNumberKey = Guid.NewGuid(), BranchCode = Branch,
            Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open, CardCode = taker, DocumentDate = Issue, DueDate = Issue.AddDays(30),
            NetAmount = 100m, InstallmentNumber = 1,
            OriginType = FinancialDocumentOrigin.TransportDocument, OriginKey = cte.Key, CreatedBy = "t", UpdatedBy = "t",
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task TwoTakers_TwoInvoices()
    {
        await CteAsync(1, "T1"); await CteAsync(2, "T1"); await CteAsync(3, "T2");

        var r = await Svc().CreateAsync(Branch, From, To, null, false, "u");

        Assert.Equal(2, r.Created.Count);
        Assert.Empty(r.Errors);
        var invs = await _db.Context.FreightInvoices.Include(i => i.Items).AsNoTracking().OrderBy(i => i.TakerCardCode).ToListAsync();
        Assert.Equal(new[] { 2, 1 }, invs.Select(i => i.Items.Count));
        Assert.All(invs, i => { Assert.Equal(FreightInvoiceStatus.Draft, i.Status); Assert.Equal(To, i.IssueDate); });
    }

    [Fact]
    public async Task NoEligible_ReturnsEmpty()
    {
        var r = await Svc().CreateAsync(Branch, From, To, null, false, "u");
        Assert.Empty(r.Created);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public async Task OneTakerFails_OthersCreated()
    {
        await CteAsync(1, "BAD"); await CteAsync(2, "T1");

        var r = await Svc().CreateAsync(Branch, From, To, null, false, "u");

        Assert.Single(r.Created);
        Assert.Single(r.Errors);
        Assert.StartsWith("BAD: ", r.Errors[0]);
        Assert.Equal(1, await _db.Context.FreightInvoices.CountAsync());
    }
}
