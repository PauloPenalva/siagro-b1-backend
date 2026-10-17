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

/// <summary>
/// Spec 2026-10-17 §1: a natureza de entrada (<c>UsageEffect.RequiresContract</c>) decide se a linha do documento de
/// entrada exige, ou recusa, o contrato de compra — e o documento não mistura os dois tipos de linha.
///
/// Backfill da migration <c>BackfillIncomingUsageRequiresContract</c> (SQL puro, sem harness relacional nos testes, ruling R1):
/// <code>
/// UPDATE e SET RequiresContract = 1 FROM USAGE_EFFECTS e JOIN USAGES u ON u.Code = e.UsageCode
/// WHERE u.Direction = 2 AND u.CreatesFinancialDocument = 1
/// </code>
/// </summary>
public class PurchaseInvoiceUsageContractRequirementTests
{
    private sealed record Scenario(UnitOfWork Db, PurchaseContract Contract, int WithContract, int WithoutContract);

    private static async Task<Scenario> SeedAsync()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync();
        var usages = new UsageService(seed.Db, NullLogger<UsageService>.Instance);
        var requiring = (await usages.CreateAsync(Usage("Compra de grãos", requiresContract: true))).Code;
        var other = (await usages.CreateAsync(Usage("Serviço tomado", requiresContract: false))).Code;
        return new Scenario(seed.Db, contract, requiring, other);
    }

    private static UsageModel Usage(string name, bool requiresContract) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        CreatesFinancialDocument = true, RequiresContract = requiresContract,
    };

    private static PurchaseInvoiceItem Line(int? usage, Guid? contract, Guid? invoiceKey = null) => new()
    {
        Key = Guid.NewGuid(), PurchaseInvoiceKey = invoiceKey, ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS",
        UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m, UsageCode = usage, PurchaseContractKey = contract,
    };

    private static PurchaseInvoice Document(PurchaseInvoiceType type, params PurchaseInvoiceItem[] lines)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier, InvoiceType = type,
            IssuerType = DocumentIssuerType.ThirdParty, TaxDocumentNumber = "6001", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 12),
        };
        foreach (var line in lines)
            invoice.AddItem(line);
        return invoice;
    }

    private static PurchaseInvoice Normal(params PurchaseInvoiceItem[] lines) => Document(PurchaseInvoiceType.Normal, lines);

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, new FakeBusinessPartnerService(), erp), new FakeDocNumberSequenceService());

    private static async Task<string> RefusalAsync(Scenario s, PurchaseInvoice invoice) =>
        (await Assert.ThrowsAsync<DefaultException>(() => Create(s.Db).ExecuteAsync(invoice, "ana"))).Message;

    [Fact]
    public async Task Line_with_usage_requiring_contract_and_no_contract_is_refused()
    {
        var s = await SeedAsync();

        var message = await RefusalAsync(s, Normal(Line(s.WithContract, null)));

        Assert.Equal("A natureza Compra de grãos exige contrato de compra na linha do produto " +
                     $"{FiscalReconciliationSeed.Product}.", message);
        Assert.Empty(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Line_with_usage_not_requiring_contract_refuses_contract()
    {
        var s = await SeedAsync();

        var message = await RefusalAsync(s, Normal(Line(s.WithoutContract, s.Contract.Key)));

        Assert.Equal("A natureza Serviço tomado não aceita contrato de compra: retire o contrato da linha do produto " +
                     $"{FiscalReconciliationSeed.Product}.", message);
    }

    [Fact]
    public async Task Mixing_contract_and_other_purchase_lines_is_refused()
    {
        var s = await SeedAsync();

        var message = await RefusalAsync(s, Normal(Line(s.WithContract, s.Contract.Key), Line(s.WithoutContract, null)));

        Assert.Equal("Um documento não mistura linhas de contrato com linhas de outras compras.", message);
    }

    [Fact]
    public async Task Compatible_lines_are_saved()
    {
        var s = await SeedAsync();

        await Create(s.Db).ExecuteAsync(Normal(Line(s.WithContract, s.Contract.Key)), "ana");

        Assert.Single(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Other_purchase_line_without_contract_is_saved()
    {
        var s = await SeedAsync();

        await Create(s.Db).ExecuteAsync(Normal(Line(s.WithoutContract, null)), "ana");

        Assert.Single(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Sapb1_mode_keeps_contract_optional()
    {
        var s = await SeedAsync();

        await Create(s.Db, "SAPB1").ExecuteAsync(Normal(Line(s.WithContract, null)), "ana");

        Assert.Single(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Value_complement_still_requires_contract()
    {
        var s = await SeedAsync();
        var line = Line(s.WithoutContract, null);
        line.Quantity = 0m;
        line.UnitPrice = 0m;
        line.ComplementAmount = 1_500m;

        var message = await RefusalAsync(s, Document(PurchaseInvoiceType.ValueComplement, line));

        Assert.Equal("Item SOJA: o complemento de valor exige o contrato de compra na linha.", message);
    }

    [Fact]
    public async Task Items_create_applies_the_usage_rule()
    {
        var s = await SeedAsync();
        var invoice = Normal(Line(s.WithoutContract, null));
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        var apply = TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService(), "STANDALONE");

        var required = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(s.Db, new FakeItemService(), apply)
                .ExecuteAsync(Line(s.WithContract, null, invoice.Key), "ana"));
        Assert.StartsWith("A natureza Compra de grãos exige contrato de compra", required.Message);

        var refused = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(s.Db, new FakeItemService(), apply)
                .ExecuteAsync(Line(s.WithoutContract, s.Contract.Key, invoice.Key), "ana"));
        Assert.StartsWith("A natureza Serviço tomado não aceita contrato de compra", refused.Message);
    }

    [Fact]
    public async Task Items_update_applies_the_usage_rule()
    {
        var s = await SeedAsync();
        var invoice = Normal(Line(s.WithoutContract, null));
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        var apply = TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService(), "STANDALONE");
        s.Db.Context.ChangeTracker.Clear();

        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        line.UsageCode = s.WithContract;
        var required = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(s.Db, new FakeItemService(), apply).ExecuteAsync(line.Key!.Value, line, "ana"));
        Assert.StartsWith("A natureza Compra de grãos exige contrato de compra", required.Message);

        s.Db.Context.ChangeTracker.Clear();
        line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        line.PurchaseContractKey = s.Contract.Key;
        var refused = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(s.Db, new FakeItemService(), apply).ExecuteAsync(line.Key!.Value, line, "ana"));
        Assert.StartsWith("A natureza Serviço tomado não aceita contrato de compra", refused.Message);
    }

    [Fact]
    public async Task Header_update_applies_the_usage_rule()
    {
        var s = await SeedAsync();
        var invoice = Normal(Line(s.WithoutContract, null));
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();

        var incoming = await s.Db.Context.PurchaseInvoices.AsNoTracking().Include(x => x.Items).SingleAsync();
        incoming.Items.Single().PurchaseContractKey = s.Contract.Key;
        s.Db.Context.ChangeTracker.Clear();

        var update = new PurchaseInvoicesUpdateService(s.Db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService(), "STANDALONE"));
        var refused = await Assert.ThrowsAsync<DefaultException>(() => update.ExecuteAsync(incoming.Key, incoming, "ana"));
        Assert.StartsWith("A natureza Serviço tomado não aceita contrato de compra", refused.Message);
    }

    [Fact]
    public void Other_purchase_predicate_needs_no_contract_and_a_financial_document()
    {
        Assert.True(PurchaseInvoiceLineGuard.IsOtherPurchase(new UsageModel { Name = "x", CreatesFinancialDocument = true }));
        Assert.False(PurchaseInvoiceLineGuard.IsOtherPurchase(
            new UsageModel { Name = "x", CreatesFinancialDocument = true, RequiresContract = true }));
        Assert.False(PurchaseInvoiceLineGuard.IsOtherPurchase(new UsageModel { Name = "x" }));
    }
}
