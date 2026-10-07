using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// O complemento fiscal do contrato nos três caminhos que gravam linha de saída (spec 2026-10-07 §4.2): criação do
/// documento (avulso e romaneio), inclusão e alteração de linha. Ele roda ANTES da natureza/CFOP/tributos — é a
/// natureza dele que o cálculo usa — e a recusa vem antes de numerar o documento.
/// </summary>
public class SalesInvoicesFiscalComplementWiringTests
{
    private const string CardBa = "C-BA";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [CardBa] = "CLIENTE BA" },
        states: new() { [CardBa] = "BA" },
        paymentConditions: new() { [CardBa] = 7 });

    private static FakeItemService Items() => new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    private sealed record Seeded(UnitOfWork Db, int DefaultUsage, int ComplementUsage, SalesContract Contract);

    /// <summary>
    /// Filial que emite NF-e, natureza padrão "Venda de grãos" e a natureza do complemento "Venda do contrato" — com
    /// CFOP diferente, para o teste enxergar qual das duas o cálculo usou.
    /// </summary>
    private static async Task<Seeded> SeedAsync(bool withComplement = true, bool issuesNfe = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA", GoodsOrigin = 0, Ncm = "12019000" });
        db.Context.IbsCbsRates.Add(new IbsCbsRate
        {
            StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m,
        });

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m, cardCode: CardBa);
        contract.Code = "CT0042";
        db.Context.SalesContracts.Add(contract);
        await db.SaveChangesAsync();

        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var defaultUsage = await usages.CreateAsync(Usage("Venda de grãos", "6102", isDefault: true));
        var complementUsage = await usages.CreateAsync(Usage("Venda do contrato", "6101", isDefault: false));

        if (withComplement)
        {
            db.Context.SalesContractFiscalComplements.Add(new SalesContractFiscalComplement
            {
                SalesContractKey = contract.Key, UsageCode = complementUsage.Code, PaymentConditionCode = 11,
                AdditionalInfo = "Pedido 77", CustomerOrderNumber = "PED-123", CustomerOrderItem = "10",
            });
            await db.SaveChangesAsync();
        }

        return new Seeded(db, defaultUsage.Code, complementUsage.Code, contract);
    }

    private static UsageModel Usage(string name, string outStateCfop, bool isDefault) => new()
    {
        Name = name, Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5102", CfopOutgoingOutState = outStateCfop,
        IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
        IcmsOutStateCst = "00",
        PisCst = "01", PisRate = 1.65m, CofinsCst = "01", CofinsRate = 7.6m,
        ExcludeIcmsFromPisCofinsBase = true,
        IbsCbsCst = "000", IbsCbsClassCode = "000001",
        RequiresQuantity = true, IsDefault = isDefault,
    };

    private static SalesInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE")
    {
        var partners = Partners();
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesCreateService(
            db, partners, Items(), new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.Apply(db, partners, erp),
            TaxTestServices.FiscalComplement(db, erp),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private static SalesInvoicesItemsCreateService ItemsCreate(UnitOfWork db) =>
        new(db, Items(), TaxTestServices.Apply(db, Partners()), TaxTestServices.FiscalComplement(db),
            NullLogger<SalesInvoicesItemsCreateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemsUpdate(UnitOfWork db) =>
        new(db, Items(),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.Apply(db, Partners()), TaxTestServices.FiscalComplement(db),
            NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, Partners(), TaxTestServices.Apply(db, Partners(), erp), TaxTestServices.FiscalComplement(db, erp),
            NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoice Invoice(Guid? contractKey, int? usageCode) => new()
    {
        Key = Guid.NewGuid(),
        BranchCode = "01",
        CardCode = CardBa,
        InvoiceType = SalesInvoiceType.Normal,
        InvoiceDate = new DateTime(2026, 10, 1),
        GrossWeight = 30000m,
        NetWeight = 30000m,
        PaymentConditionCode = 5,
        TaxPayerComments = "Placa ABC",
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG",
                Quantity = 30000m, UnitPrice = 2m, SalesContractKey = contractKey, UsageCode = usageCode,
            },
        ],
    };

    // ---------------------------------------------------------------- criação do documento

    [Fact]
    public async Task Create_applies_the_complement_before_usage_cfop_and_taxes()
    {
        var s = await SeedAsync();
        var invoice = Invoice(s.Contract.Key, usageCode: s.DefaultUsage);

        await Create(s.Db).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();
        Assert.Equal(s.ComplementUsage, item.UsageCode);
        Assert.Equal("Venda do contrato", item.UsageName);
        Assert.Equal("6101", item.Cfop); // o CFOP da natureza do complemento, não o da natureza do corpo
        Assert.Equal("PED-123", item.CustomerOrderNumber);
        Assert.Equal("10", item.CustomerOrderItem);
        // A condição do complemento vence o corpo (5) e a do cliente (7).
        Assert.Equal(11, invoice.PaymentConditionCode);
        Assert.Equal("Pedido 77 | Placa ABC", invoice.TaxPayerComments);
    }

    /// <summary>Natureza inexistente no corpo: só passa porque o complemento a troca ANTES da guarda da natureza.</summary>
    [Fact]
    public async Task Create_runs_the_complement_before_the_usage_guard()
    {
        var s = await SeedAsync();
        var invoice = Invoice(s.Contract.Key, usageCode: 999);

        await Create(s.Db).ExecuteAsync(invoice, "tester");

        Assert.Equal(s.ComplementUsage, invoice.Items.Single().UsageCode);
    }

    [Fact]
    public async Task Create_without_complement_is_refused_before_numbering_and_writing()
    {
        var s = await SeedAsync(withComplement: false);
        var invoice = Invoice(s.Contract.Key, usageCode: s.DefaultUsage);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(s.Db).ExecuteAsync(invoice, "tester"));

        Assert.Contains("CT0042", e.Message);
        Assert.Null(invoice.DocNumberKey);
        Assert.True(string.IsNullOrEmpty(invoice.InvoiceNumber));
        Assert.Equal(0, await s.Db.Context.SalesInvoices.CountAsync());
    }

    [Fact]
    public async Task Create_with_rule_inactive_keeps_the_body_and_the_customer_condition_rule()
    {
        var s = await SeedAsync(withComplement: false, issuesNfe: false);
        var invoice = Invoice(s.Contract.Key, usageCode: s.DefaultUsage);
        invoice.PaymentConditionCode = null;

        await Create(s.Db).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();
        Assert.Equal(s.DefaultUsage, item.UsageCode);
        Assert.Null(item.CustomerOrderNumber);
        Assert.Equal(7, invoice.PaymentConditionCode); // condição padrão do cliente, como hoje
        Assert.Equal("Placa ABC", invoice.TaxPayerComments);
    }

    [Fact]
    public async Task Create_in_sapb1_mode_keeps_the_body()
    {
        var s = await SeedAsync(withComplement: false);
        var invoice = Invoice(s.Contract.Key, usageCode: s.DefaultUsage);

        await Create(s.Db, "SAPB1").ExecuteAsync(invoice, "tester");

        Assert.Equal(s.DefaultUsage, invoice.Items.Single().UsageCode);
        Assert.Equal(5, invoice.PaymentConditionCode);
    }

    // ---------------------------------------------------------------- linhas

    private static async Task<SalesInvoice> PendingInvoiceAsync(UnitOfWork db, int? paymentCondition)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = CardBa, InvoiceStatus = InvoiceStatus.Pending,
            InvoiceType = SalesInvoiceType.Normal, InvoiceDate = new DateTime(2026, 10, 1),
            PaymentConditionCode = paymentCondition,
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private static SalesInvoiceItem Line(Guid invoiceKey, Guid? contractKey, int usageCode) => new()
    {
        Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
        Quantity = 1000m, UnitPrice = 2m, SalesContractKey = contractKey, UsageCode = usageCode,
    };

    [Fact]
    public async Task Adding_a_contract_line_applies_the_complement_before_the_tax()
    {
        var s = await SeedAsync();
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: null);
        var line = Line(invoice.Key, s.Contract.Key, s.DefaultUsage);

        await ItemsCreate(s.Db).ExecuteAsync(line, "tester");

        var stored = await s.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal(s.ComplementUsage, stored.UsageCode);
        Assert.Equal("6101", stored.Cfop);
        Assert.Equal("PED-123", stored.CustomerOrderNumber);
        Assert.Equal(11, (await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).PaymentConditionCode);
    }

    [Fact]
    public async Task Adding_a_contract_line_without_complement_writes_nothing()
    {
        var s = await SeedAsync(withComplement: false);
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: null);

        await Assert.ThrowsAsync<DefaultException>(() =>
            ItemsCreate(s.Db).ExecuteAsync(Line(invoice.Key, s.Contract.Key, s.DefaultUsage), "tester"));

        Assert.Equal(0, await s.Db.Context.SalesInvoicesItems.CountAsync());
    }

    [Fact]
    public async Task Adding_a_line_without_contract_keeps_its_usage()
    {
        var s = await SeedAsync(withComplement: false);
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: 5);
        var line = Line(invoice.Key, contractKey: null, s.DefaultUsage);

        await ItemsCreate(s.Db).ExecuteAsync(line, "tester");

        var stored = await s.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal(s.DefaultUsage, stored.UsageCode);
        Assert.Equal("6102", stored.Cfop);
    }

    /// <summary>Como o Delta do PATCH: a linha rastreada chega mutada com a natureza do corpo; o complemento a troca de volta antes do imposto.</summary>
    [Fact]
    public async Task Patch_that_changes_the_usage_of_a_contract_line_gets_the_complement_back()
    {
        var s = await SeedAsync();
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: 11);
        var line = Line(invoice.Key, s.Contract.Key, s.ComplementUsage);
        await ItemsCreate(s.Db).ExecuteAsync(line, "tester");

        line.UsageCode = s.DefaultUsage;
        line.CustomerOrderNumber = "OUTRO";
        await ItemsUpdate(s.Db).ExecuteAsync(line.Key!.Value, line, "tester");

        var stored = await s.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal(s.ComplementUsage, stored.UsageCode);
        Assert.Equal("6101", stored.Cfop);
        Assert.Equal("PED-123", stored.CustomerOrderNumber);
    }

    [Fact]
    public async Task Patch_on_a_document_with_another_condition_overwrites_it_when_no_other_contract_is_stored()
    {
        var s = await SeedAsync();
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: 11);
        var line = Line(invoice.Key, s.Contract.Key, s.ComplementUsage);
        await ItemsCreate(s.Db).ExecuteAsync(line, "tester");

        var tracked = await s.Db.Context.SalesInvoices.SingleAsync();
        tracked.PaymentConditionCode = 5;
        await s.Db.SaveChangesAsync();

        line.Quantity = 500m;
        await ItemsUpdate(s.Db).ExecuteAsync(line.Key!.Value, line, "tester");

        // D6: o complemento manda; só há esta linha de contrato gravada, então não há conflito a recusar.
        var stored = await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(11, stored.PaymentConditionCode);
    }

    // ---------------------------------------------------------------- cabeçalho

    /// <summary>
    /// D6 também na edição do cabeçalho: o PATCH reenvia a entidade inteira, então a condição e o texto do contrato
    /// voltam em silêncio (sem recusa) quando o documento tem linha de contrato.
    /// </summary>
    [Fact]
    public async Task Header_patch_restores_the_condition_and_the_contract_text()
    {
        var s = await SeedAsync();
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: null);
        await ItemsCreate(s.Db).ExecuteAsync(Line(invoice.Key, s.Contract.Key, s.DefaultUsage), "tester");

        var tracked = await s.Db.Context.SalesInvoices.SingleAsync();
        tracked.PaymentConditionCode = 5;
        tracked.TaxPayerComments = "Placa XYZ";
        await HeaderUpdate(s.Db).ExecuteAsync(tracked.Key, tracked, "tester");

        var stored = await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(11, stored.PaymentConditionCode);
        Assert.Equal("Pedido 77 | Placa XYZ", stored.TaxPayerComments);
    }

    [Fact]
    public async Task Header_patch_in_sapb1_mode_keeps_the_new_condition()
    {
        var s = await SeedAsync();
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: 11);
        s.Db.Context.SalesInvoicesItems.Add(Line(invoice.Key, s.Contract.Key, s.DefaultUsage));
        await s.Db.SaveChangesAsync();

        var tracked = await s.Db.Context.SalesInvoices.SingleAsync();
        tracked.PaymentConditionCode = 5;
        await HeaderUpdate(s.Db, "SAPB1").ExecuteAsync(tracked.Key, tracked, "tester");

        var stored = await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(5, stored.PaymentConditionCode);
        Assert.Null(stored.TaxPayerComments);
    }

    [Fact]
    public async Task Header_patch_on_a_document_without_contract_lines_keeps_the_new_condition()
    {
        var s = await SeedAsync();
        var invoice = await PendingInvoiceAsync(s.Db, paymentCondition: 11);
        await ItemsCreate(s.Db).ExecuteAsync(Line(invoice.Key, contractKey: null, s.DefaultUsage), "tester");

        var tracked = await s.Db.Context.SalesInvoices.SingleAsync();
        tracked.PaymentConditionCode = 5;
        await HeaderUpdate(s.Db).ExecuteAsync(tracked.Key, tracked, "tester");

        var stored = await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(5, stored.PaymentConditionCode);
        Assert.Null(stored.TaxPayerComments);
    }
}
