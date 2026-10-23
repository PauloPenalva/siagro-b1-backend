using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Entrega B do CT-e: o título a receber nasce na autorização, contra o TOMADOR, pela condição de pagamento.</summary>
public class FinancialDocumentsTransportServiceTests
{
    private static readonly DateTime Issue = new(2026, 10, 22, 10, 0, 0);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeDocNumberSequenceService _numbers = new();

    private FinancialDocumentsTransportService Service() => FinancialDocumentTestServices.Transport(_db, _numbers);

    private async Task<int> UsageAsync(bool financial)
    {
        var usages = new UsageService(_db, NullLogger<UsageService>.Instance);
        return (await usages.CreateAsync(new UsageModel
        {
            Name = financial ? "Frete" : "Frete sem financeiro", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5352", CfopOutgoingOutState = "6352",
            ContractBalanceEffect = ContractBalanceEffect.None, RequiresContract = false,
            CreatesFinancialDocument = financial,
        })).Code;
    }

    private async Task<int> ConditionAsync(string days)
    {
        var condition = new PaymentCondition { Name = days, Days = days, PaymentMeans = "15" };
        _db.Context.PaymentConditions.Add(condition);
        await _db.SaveChangesAsync();
        return condition.Code;
    }

    /// <summary>CT-e autorizado com tomador = destinatário (TakerRole 3).</summary>
    internal static TransportDocument Doc(int usage, int? condition) => new()
    {
        BranchCode = "01", Series = 1, Number = 100, Status = CteStatus.Authorized, IssuedAt = Issue,
        UsageCode = usage, TakerRole = 3, AmountToReceive = 1000m, ServiceTotal = 1000m, PaymentConditionCode = condition,
        CostCenterCode = "CC01", AccountingItemCode = "IT01", DistributionRuleCode = null,
        Parties =
        [
            new TransportDocumentParty { Role = CtePartyRole.Sender, CardCode = "REM", Name = "REMETENTE LTDA" },
            new TransportDocumentParty { Role = CtePartyRole.Recipient, CardCode = "DES", Name = "DESTINATARIO LTDA" },
        ],
    };

    private Task<List<FinancialDocument>> TitlesAsync(Guid key) =>
        _db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.TransportDocument && d.OriginKey == key)
            .OrderBy(d => d.InstallmentNumber).ToListAsync();

    [Fact]
    public async Task Authorized_cte_creates_receivable_for_taker_with_installments_and_dimensions()
    {
        var doc = Doc(await UsageAsync(true), await ConditionAsync("30,60"));
        _db.Context.TransportDocuments.Add(doc);
        await _db.SaveChangesAsync();

        await Service().EnqueueAsync(doc, "ana");
        await _db.SaveChangesAsync();
        // Idempotente: a segunda chamada não duplica.
        await Service().EnqueueAsync(doc, "ana");
        await _db.SaveChangesAsync();

        var titles = await TitlesAsync(doc.Key);
        Assert.Equal(new[] { 500m, 500m }, titles.Select(t => t.NetAmount));
        Assert.Equal(new[] { Issue.Date.AddDays(30), Issue.Date.AddDays(60) }, titles.Select(t => t.DueDate));
        Assert.All(titles, t =>
        {
            Assert.Equal("DES", t.CardCode);
            Assert.Equal("DESTINATARIO LTDA", t.CardName);
            Assert.Equal(FinancialDirection.Receivable, t.Direction);
            Assert.Equal(FinancialDocumentNature.Firm, t.Nature);
            Assert.Equal(FinancialDocumentStatus.Open, t.Status);
            Assert.Equal("CC01", t.CostCenterCode);
            Assert.Equal("IT01", t.AccountingItemCode);
            Assert.Equal("01", t.BranchCode);
            Assert.Equal("100", t.OriginDocNumber);
        });
    }

    [Fact]
    public async Task Usage_without_financial_creates_nothing()
    {
        var doc = Doc(await UsageAsync(false), null);
        _db.Context.TransportDocuments.Add(doc);
        await _db.SaveChangesAsync();

        await Service().EnqueueAsync(doc, "ana");
        await _db.SaveChangesAsync();

        Assert.Empty(await TitlesAsync(doc.Key));
    }

    [Fact]
    public async Task Undo_with_settled_title_is_refused_and_loose_title_is_cancelled()
    {
        var doc = Doc(await UsageAsync(true), null);
        _db.Context.TransportDocuments.Add(doc);
        await _db.SaveChangesAsync();
        await Service().EnqueueAsync(doc, "ana");
        await _db.SaveChangesAsync();

        await Service().EnsureCanUndoAsync(doc.Key);
        Service().EnqueueUndo(doc.Key, "ana");
        await _db.SaveChangesAsync();

        Assert.All(await TitlesAsync(doc.Key), t => Assert.Equal(FinancialDocumentStatus.Canceled, t.Status));

        var other = Doc(await UsageAsync(true), null);
        _db.Context.TransportDocuments.Add(other);
        await _db.SaveChangesAsync();
        await Service().EnqueueAsync(other, "ana");
        await _db.SaveChangesAsync();
        var title = await _db.Context.FinancialDocuments.SingleAsync(d => d.OriginKey == other.Key);
        title.SettledAmount = 100m;
        await _db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().EnsureCanUndoAsync(other.Key));
        Assert.Contains("já tem baixa", e.Message);
    }
}
