using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-13 D11: documento de entrada "Complemento de valor" — linhas só com valor, contrato e natureza.</summary>
public class PurchaseInvoiceValueComplementTests
{
    private sealed record Scenario(UnitOfWork Db, PurchaseContract Contract, int Financial, int NonFinancial);

    private static async Task<Scenario> SeedAsync()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync();
        var usages = new UsageService(seed.Db, NullLogger<UsageService>.Instance);
        var financial = (await usages.CreateAsync(Usage("Complemento de preço", true))).Code;
        var nonFinancial = (await usages.CreateAsync(Usage("Entrada sem financeiro", false))).Code;
        return new Scenario(seed.Db, contract, financial, nonFinancial);
    }

    private static UsageModel Usage(string name, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        CreatesFinancialDocument = createsFinancial,
    };

    private static PurchaseInvoice Document(
        Scenario s, int? usageCode, decimal amount = 1_500m, decimal quantity = 0m,
        PurchaseInvoiceType type = PurchaseInvoiceType.ValueComplement,
        DocumentIssuerType issuer = DocumentIssuerType.ThirdParty, bool withContract = true)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier, InvoiceType = type,
            IssuerType = issuer, TaxDocumentNumber = "5001", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 12),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            Quantity = quantity, UnitPrice = quantity > 0 ? 2m : 0m, ComplementAmount = amount, UsageCode = usageCode,
            PurchaseContractKey = withContract ? s.Contract.Key : null,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, new FakeBusinessPartnerService(), erp), new FakeDocNumberSequenceService());

    private static async Task<string> RefusalAsync(Scenario s, PurchaseInvoice invoice, string erp = "STANDALONE") =>
        (await Assert.ThrowsAsync<DefaultException>(() => Create(s.Db, erp).ExecuteAsync(invoice, "ana"))).Message;

    [Fact]
    public void Complement_line_total_is_its_amount()
    {
        var line = new PurchaseInvoiceItem { ComplementAmount = 1_500m, FreightValue = 10m };

        Assert.Equal(1_500m, line.Total);
        Assert.Equal(1_510m, line.GrandTotal);
    }

    [Fact]
    public async Task Valid_value_complement_is_saved()
    {
        var s = await SeedAsync();

        await Create(s.Db, "STANDALONE").ExecuteAsync(Document(s, s.Financial), "ana");

        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(1_500m, line.ComplementAmount);
        Assert.Equal(PurchaseInvoiceType.ValueComplement,
            (await s.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).InvoiceType);
    }

    [Fact]
    public async Task Sapb1_accepts_a_nature_without_the_flag()
    {
        var s = await SeedAsync();

        await Create(s.Db, "SAPB1").ExecuteAsync(Document(s, s.NonFinancial), "ana");

        Assert.Single(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Value_complement_refusals()
    {
        var s = await SeedAsync();

        Assert.Equal("Item SOJA: no complemento de valor a linha não tem quantidade nem preço unitário; informe só o valor do complemento.",
            await RefusalAsync(s, Document(s, s.Financial, quantity: 10m)));
        Assert.Equal("Item SOJA: informe o valor do complemento.",
            await RefusalAsync(s, Document(s, s.Financial, amount: 0m)));
        Assert.Equal("Item SOJA: o complemento de valor exige o contrato de compra na linha.",
            await RefusalAsync(s, Document(s, s.Financial, withContract: false)));
        Assert.Equal("Item SOJA: o complemento de valor exige a natureza de operação.",
            await RefusalAsync(s, Document(s, null)));
        Assert.Equal($"Item SOJA: a natureza {s.NonFinancial} não gera financeiro; o complemento de valor precisa de uma natureza que gere.",
            await RefusalAsync(s, Document(s, s.NonFinancial)));
        // Spec 2026-10-14: a recusa do complemento próprio saiu; sem o item da nota original, C1.
        Assert.Equal("Item SOJA: o complemento de valor de emissão própria exige o item da nota original.",
            await RefusalAsync(s, Document(s, s.Financial, issuer: DocumentIssuerType.Own)));
        Assert.Empty(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Other_types_cannot_carry_a_complement_amount()
    {
        var s = await SeedAsync();

        Assert.Equal("Item SOJA: valor de complemento só existe no documento do tipo Complemento de valor.",
            await RefusalAsync(s, Document(s, s.Financial, quantity: 1_000m, type: PurchaseInvoiceType.Normal)));
    }

    [Fact]
    public async Task Line_create_and_update_apply_the_same_rules()
    {
        var s = await SeedAsync();
        var invoice = Document(s, s.Financial);
        await Create(s.Db, "STANDALONE").ExecuteAsync(invoice, "ana");
        var apply = TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService(), "STANDALONE");

        var extra = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key, ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG", Quantity = 5m, UnitPrice = 2m, UsageCode = s.Financial, PurchaseContractKey = s.Contract.Key,
        };
        var created = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(s.Db, new FakeItemService(), apply).ExecuteAsync(extra, "ana"));
        Assert.StartsWith("Item SOJA: no complemento de valor a linha não tem quantidade", created.Message);

        s.Db.Context.ChangeTracker.Clear();
        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        line.PurchaseContractKey = null;
        var updated = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(s.Db, new FakeItemService(), apply).ExecuteAsync(line.Key!.Value, line, "ana"));
        Assert.Equal("Item SOJA: o complemento de valor exige o contrato de compra na linha.", updated.Message);
    }
}
