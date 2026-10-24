using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FreightInvoices;

public class FreightInvoicesSaveServiceTests
{
    private const string Branch = "01";
    private static readonly DateTime Issue = new(2026, 10, 22, 10, 0, 0);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FreightInvoicesSaveService Svc(string erp = "STANDALONE") =>
        new(_db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["DES"] = "Destinatário" }),
            new FreightInvoiceEligibleCtesQuery(_db), TestConfiguration.Erp(erp));

    private async Task<TransportDocument> CteAsync(int number, decimal amount = 1000m, int? paymentCondition = null,
        CteStatus status = CteStatus.Authorized)
    {
        var cte = new TransportDocument
        {
            BranchCode = Branch, Series = 1, Number = number, AccessKey = new string('1', 44), Status = status, IssuedAt = Issue,
            TakerRole = 3, PaymentConditionCode = paymentCondition,
            Parties = [new TransportDocumentParty { Role = CtePartyRole.Recipient, CardCode = "DES", Name = "DES" }],
        };
        _db.Context.TransportDocuments.Add(cte);
        _db.Context.FinancialDocuments.Add(new FinancialDocument
        {
            Code = Guid.NewGuid().ToString("N")[..12], DocNumberKey = Guid.NewGuid(), BranchCode = Branch,
            Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open, CardCode = "DES", DocumentDate = Issue, DueDate = Issue.AddDays(30),
            NetAmount = amount, InstallmentNumber = 1,
            OriginType = FinancialDocumentOrigin.TransportDocument, OriginKey = cte.Key, CreatedBy = "t", UpdatedBy = "t",
        });
        await _db.SaveChangesAsync();
        return cte;
    }

    private static FreightInvoiceSaveModel Model(Guid? key, params Guid[] ctes) =>
        new(key, Branch, "DES", Issue, null, null, null, ctes, null, null);

    [Fact]
    public async Task Create_Draft_TotalIsSum()
    {
        var a = await CteAsync(1, 1000m, paymentCondition: 7);
        var b = await CteAsync(2, 250.50m);

        var inv = await Svc().SaveAsync(Model(null, a.Key, b.Key), "u");

        Assert.Equal(FreightInvoiceStatus.Draft, inv.Status);
        Assert.Equal(1250.50m, inv.TotalAmount);
        Assert.Equal("ST-0001", inv.Code);
        Assert.Equal("Destinatário", inv.TakerName);
        Assert.Equal(7, inv.PaymentConditionCode);
        Assert.Equal(new[] { 1, 2 }, inv.Items.OrderBy(i => i.LineNum).Select(i => i.LineNum));
    }

    [Fact]
    public async Task NoCte_Rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc().SaveAsync(Model(null), "u"));
        Assert.Equal("Selecione ao menos um CT-e.", ex.Message);
    }

    [Fact]
    public async Task NotStandalone_Rejected()
    {
        var a = await CteAsync(1);
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc("SAPB1").SaveAsync(Model(null, a.Key), "u"));
        Assert.Equal(SiagroB1.Application.Services.Cte.BranchCteSettingsService.StandaloneOnly, ex.Message);
    }

    [Fact]
    public async Task IneligibleCte_Rejected()
    {
        var a = await CteAsync(55, status: CteStatus.Draft);
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc().SaveAsync(Model(null, a.Key), "u"));
        Assert.StartsWith("CT-e 55 não pode ser faturado: ", ex.Message);
    }

    [Fact]
    public async Task CteInOtherActiveInvoice_Rejected()
    {
        var a = await CteAsync(9);
        await Svc().SaveAsync(Model(null, a.Key), "u");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc().SaveAsync(Model(null, a.Key), "u"));
        Assert.StartsWith("CT-e 9 não pode ser faturado: ", ex.Message);
        Assert.Equal(1, await _db.Context.FreightInvoices.CountAsync());
    }

    [Fact]
    public async Task Update_Issued_Rejected()
    {
        var a = await CteAsync(1);
        var inv = await Svc().SaveAsync(Model(null, a.Key), "u");
        inv.Status = FreightInvoiceStatus.Issued;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Svc().SaveAsync(Model(inv.Key, a.Key), "u"));
    }

    [Fact]
    public async Task Resave_Draft_SameCte_Allowed()
    {
        var a = await CteAsync(1);
        var inv = await Svc().SaveAsync(Model(null, a.Key), "u");
        var again = await Svc().SaveAsync(Model(inv.Key, a.Key), "u");
        Assert.Equal(inv.Key, again.Key);
        Assert.Single(await _db.Context.FreightInvoiceItems.ToListAsync());
    }

    [Fact]
    public async Task RemoveCte_FromDraft_Frees()
    {
        var a = await CteAsync(1, 100m);
        var b = await CteAsync(2, 200m);
        var inv = await Svc().SaveAsync(Model(null, a.Key, b.Key), "u");

        await Svc().SaveAsync(Model(inv.Key, a.Key), "u");

        Assert.Equal(100m, (await _db.Context.FreightInvoices.SingleAsync()).TotalAmount);
        Assert.Equal(a.Key, Assert.Single(await _db.Context.FreightInvoiceItems.ToListAsync()).TransportDocumentKey);
        var other = await Svc().SaveAsync(Model(null, b.Key), "u");
        Assert.Equal(200m, other.TotalAmount);
    }
}
