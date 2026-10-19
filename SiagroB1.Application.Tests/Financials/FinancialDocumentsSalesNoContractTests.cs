using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Spec 2026-10-19 parte 1: o documento de saída confirmado gera título a receber pela NATUREZA, com ou sem contrato.
/// Linha sem contrato (venda de ativo, sacaria, serviço de armazenagem) → parcelas da condição de pagamento do documento.
/// </summary>
public class FinancialDocumentsSalesNoContractTests
{
    private static readonly DateTime Issue = new(2026, 10, 15);

    internal sealed class Fixture
    {
        public SalesFirmReceivableSeed Seed { get; private init; } = null!;
        public int NoContractUsage { get; private set; }
        public int NoContractNoFinancialUsage { get; private set; }
        public string? Cutover { get; private init; }

        public static async Task<Fixture> CreateAsync(string erp = "STANDALONE", string? cutover = "2026-10-01")
        {
            var f = new Fixture { Seed = await SalesFirmReceivableSeed.CreateAsync(erp: erp), Cutover = cutover };
            var usages = new UsageService(f.Seed.Db, NullLogger<UsageService>.Instance);
            f.NoContractUsage = (await usages.CreateAsync(Usage("Venda de ativo", true))).Code;
            f.NoContractNoFinancialUsage = (await usages.CreateAsync(Usage("Remessa sem financeiro", false))).Code;
            return f;
        }

        private static UsageModel Usage(string name, bool financial) => new()
        {
            Name = name, Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5551", CfopOutgoingOutState = "6551",
            ContractBalanceEffect = ContractBalanceEffect.None, RequiresContract = false,
            CreatesFinancialDocument = financial,
        };

        public FinancialDocumentsSalesNoContractService Service() =>
            FinancialDocumentTestServices.SalesNoContract(Seed.Db, Seed.Numbers, Seed.Erp, Cutover);

        public async Task<int> AddConditionAsync(string days)
        {
            var condition = new PaymentCondition { Name = days, Days = days, PaymentMeans = "15" };
            Seed.Context.PaymentConditions.Add(condition);
            await Seed.Context.SaveChangesAsync();
            return condition.Code;
        }

        public async Task<SalesInvoice> NoContractInvoiceAsync(
            decimal amount = 1000m, int? condition = null, int? usage = null, DateTime? date = null,
            Action<SalesInvoice>? tweak = null)
        {
            var (invoice, _) = await Seed.AddInvoiceAsync(1m, amount, withContract: false, usageCode: usage ?? NoContractUsage);
            var tracked = await Seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
            tracked.PaymentConditionCode = condition;
            tracked.InvoiceDate = date ?? Issue;
            tweak?.Invoke(tracked);
            await Seed.Context.SaveChangesAsync();
            return tracked;
        }

        public Task ConfirmAsync(SalesInvoice invoice) =>
            Seed.Confirm(salesNoContract: Service()).ExecuteAsync(invoice.Key, "ana");

        public Task<List<FinancialDocument>> TitlesAsync(SalesInvoice invoice) =>
            Seed.Context.FinancialDocuments.AsNoTracking()
                .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoiceNoContract && d.OriginKey == invoice.Key)
                .OrderBy(d => d.InstallmentNumber).ToListAsync();
    }

    [Fact]
    public async Task Line_without_contract_and_nature_with_financial_creates_receivable()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.NoContractInvoiceAsync(condition: await f.AddConditionAsync("30,60"));

        await f.ConfirmAsync(invoice);

        var titles = await f.TitlesAsync(invoice);
        Assert.Equal(new[] { 500m, 500m }, titles.Select(t => t.NetAmount));
        Assert.Equal(new[] { Issue.AddDays(30), Issue.AddDays(60) }, titles.Select(t => t.DueDate));
        Assert.Equal(new int?[] { 1, 2 }, titles.Select(t => t.InstallmentNumber));
        Assert.All(titles, t =>
        {
            Assert.Equal(FinancialDirection.Receivable, t.Direction);
            Assert.Equal(FinancialDocumentNature.Firm, t.Nature);
            Assert.Equal(FinancialDocumentStatus.Open, t.Status);
            Assert.Null(t.SalesContractKey);
            Assert.Equal(invoice.Key, t.SalesInvoiceKey);
            Assert.Equal(invoice.CardCode, t.CardCode);
        });
    }

    [Fact]
    public async Task No_payment_condition_creates_single_title_due_on_issue_date()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.NoContractInvoiceAsync();

        await f.ConfirmAsync(invoice);

        var title = Assert.Single(await f.TitlesAsync(invoice));
        Assert.Equal(1000m, title.NetAmount);
        Assert.Equal(Issue, title.DueDate);
    }

    [Fact]
    public async Task Nature_without_financial_creates_nothing()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.NoContractInvoiceAsync(usage: f.NoContractNoFinancialUsage);

        await f.ConfirmAsync(invoice);

        Assert.Empty(await f.TitlesAsync(invoice));
    }

    [Fact]
    public async Task Invoice_before_cutover_creates_nothing()
    {
        var f = await Fixture.CreateAsync(cutover: "2026-10-20");
        var invoice = await f.NoContractInvoiceAsync(date: new DateTime(2026, 10, 19));

        await f.ConfirmAsync(invoice);

        Assert.Empty(await f.TitlesAsync(invoice));
    }

    [Fact]
    public async Task Missing_cutover_key_applies_to_all()
    {
        var f = await Fixture.CreateAsync(cutover: null);
        var invoice = await f.NoContractInvoiceAsync(date: new DateTime(2020, 1, 1));

        await f.ConfirmAsync(invoice);

        Assert.Single(await f.TitlesAsync(invoice));
    }

    [Theory]
    [InlineData("icms")]
    [InlineData("custody")]
    [InlineData("release")]
    public async Task Icms_complement_custody_and_release_documents_create_nothing(string kind)
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.NoContractInvoiceAsync(tweak: i =>
        {
            switch (kind)
            {
                case "icms": i.InvoiceType = SalesInvoiceType.IcmsComplement; break;
                case "custody": i.CustodyRole = StorageCustodyRole.StorageReturn; break;
                default: i.ShipmentReleaseKey = Guid.NewGuid(); break;
            }
        });
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;

        await f.Service().EnqueueAsync(invoice, "ana");
        await f.Seed.Db.SaveChangesAsync();

        Assert.Empty(await f.TitlesAsync(invoice));
    }

    [Fact]
    public async Task Mixed_document_splits_contract_and_no_contract_amounts()
    {
        var f = await Fixture.CreateAsync();
        var (invoice, contractLine) = await f.Seed.AddInvoiceAsync(1m, 700m);
        var tracked = await f.Seed.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == invoice.Key);
        var free = SalesContractsAllocationTestSupport.NewItem(tracked, null, null, 1m, 300m);
        free.UsageCode = f.NoContractUsage;
        f.Seed.Context.SalesInvoicesItems.Add(free);
        await f.Seed.Context.SaveChangesAsync();

        await f.ConfirmAsync(invoice);

        var firm = Assert.Single(await f.Seed.TitlesOfAsync(contractLine));
        Assert.Equal(700m, firm.NetAmount);
        Assert.Equal(300m, (await f.TitlesAsync(invoice)).Sum(t => t.NetAmount));
    }

    [Fact]
    public async Task Sapb1_line_without_contract_after_cutover_creates_receivable()
    {
        var f = await Fixture.CreateAsync(erp: "SAPB1");
        var invoice = await f.NoContractInvoiceAsync();
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        await f.Seed.Context.SaveChangesAsync();

        await f.Service().EnqueueAsync(invoice, "ana");
        await f.Seed.Db.SaveChangesAsync();

        Assert.Single(await f.TitlesAsync(invoice));
    }

    [Fact]
    public async Task Requires_contract_does_not_decide_financial()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.NoContractInvoiceAsync();   // natureza RequiresContract=false, CreatesFinancialDocument=true

        await f.ConfirmAsync(invoice);

        Assert.NotEmpty(await f.TitlesAsync(invoice));
    }

    [Fact]
    public async Task Enqueue_is_idempotent()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.NoContractInvoiceAsync();
        await f.ConfirmAsync(invoice);

        var reloaded = await f.Seed.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == invoice.Key);
        await f.Service().EnqueueAsync(reloaded, "ana");
        await f.Seed.Db.SaveChangesAsync();

        Assert.Single(await f.TitlesAsync(invoice));
    }
}
