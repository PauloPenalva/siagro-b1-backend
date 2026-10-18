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
/// Spec 2026-10-17 (complementos): o documento "Complemento de ICMS" (K1–K4, K3 só STANDALONE), a natureza mandando no
/// contrato também nos complementos (D3) e a regra do complemento próprio (C1–C10) para o ICMS.
/// </summary>
public class PurchaseInvoiceIcmsComplementTests
{
    private const string K1 =
        "Item SOJA: no complemento de ICMS a linha não tem quantidade, preço nem valor; informe só a base e o valor do ICMS.";
    private const string K2 = "Item SOJA: informe a base e o valor do ICMS do complemento.";
    private const string K3 = "Complemento de ICMS só existe no modo STANDALONE.";
    private const string K4 = "Item SOJA: o ICMS do complemento não pode ser maior que a base.";

    /// <summary>Naturezas: de contrato (gera financeiro), de outra compra (sem contrato, gera) e sem financeiro (sem contrato).</summary>
    private sealed record Scenario(UnitOfWork Db, PurchaseContract Contract, int ContractUsage, int OtherPurchase, int NonFinancial);

    private static async Task<Scenario> SeedAsync()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync();
        var usages = new UsageService(seed.Db, NullLogger<UsageService>.Instance);
        var contractUsage = (await usages.CreateAsync(Usage("Compra de grãos", true, true))).Code;
        var otherPurchase = (await usages.CreateAsync(Usage("Serviço tomado", false, true))).Code;
        var nonFinancial = (await usages.CreateAsync(Usage("Complemento sem financeiro", false, false))).Code;
        return new Scenario(seed.Db, contract, contractUsage, otherPurchase, nonFinancial);
    }

    private static UsageModel Usage(string name, bool requiresContract, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        RequiresContract = requiresContract, CreatesFinancialDocument = createsFinancial,
    };

    private static PurchaseInvoice Document(
        PurchaseInvoiceType type, int? usageCode, Guid? contractKey = null, decimal quantity = 0m, decimal price = 0m,
        decimal amount = 0m, decimal icmsBase = 1_000m, decimal icmsValue = 120m)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier, InvoiceType = type,
            IssuerType = DocumentIssuerType.ThirdParty, TaxDocumentNumber = "7001", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 17),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            Quantity = quantity, UnitPrice = price, ComplementAmount = amount, IcmsBase = icmsBase, IcmsValue = icmsValue,
            UsageCode = usageCode, PurchaseContractKey = contractKey,
        });
        return invoice;
    }

    private static PurchaseInvoice Icms(int? usageCode, Guid? contractKey = null, decimal quantity = 0m, decimal price = 0m,
        decimal amount = 0m, decimal icmsBase = 1_000m, decimal icmsValue = 120m) =>
        Document(PurchaseInvoiceType.IcmsComplement, usageCode, contractKey, quantity, price, amount, icmsBase, icmsValue);

    private static PurchaseInvoice Value(int? usageCode, Guid? contractKey) =>
        Document(PurchaseInvoiceType.ValueComplement, usageCode, contractKey, amount: 1_500m, icmsBase: 0m, icmsValue: 0m);

    private static PurchaseInvoicesTaxApplyService Apply(UnitOfWork db, string erp) =>
        TaxTestServices.PurchaseApply(db, new FakeBusinessPartnerService(), erp);

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), Apply(db, erp), new FakeDocNumberSequenceService());

    private static async Task<string> RefusalAsync(Scenario s, PurchaseInvoice invoice, string erp = "STANDALONE") =>
        (await Assert.ThrowsAsync<DefaultException>(() => Create(s.Db, erp).ExecuteAsync(invoice, "ana"))).Message;

    private static string RequiresContract(string usageName) =>
        $"A natureza {usageName} exige contrato de compra na linha do produto {FiscalReconciliationSeed.Product}.";

    [Fact]
    public async Task Icms_complement_with_base_and_value_is_saved()
    {
        var s = await SeedAsync();

        await Create(s.Db).ExecuteAsync(Icms(s.NonFinancial), "ana");

        var invoice = await s.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        Assert.Equal(PurchaseInvoiceType.IcmsComplement, invoice.InvoiceType);
        var line = Assert.Single(invoice.Items);
        Assert.Equal(0m, line.Quantity);
        Assert.Equal(0m, line.ComplementAmount);
        Assert.Null(line.PurchaseContractKey);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public async Task Icms_complement_refuses_quantity_price_or_amount(int quantity, int price, int amount)
    {
        var s = await SeedAsync();

        Assert.Equal(K1, await RefusalAsync(s, Icms(s.NonFinancial, quantity: quantity, price: price, amount: amount)));
        Assert.Empty(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(1000, 0)]
    public async Task Icms_complement_refuses_missing_base_or_value(int icmsBase, int icmsValue)
    {
        var s = await SeedAsync();

        Assert.Equal(K2, await RefusalAsync(s, Icms(s.NonFinancial, icmsBase: icmsBase, icmsValue: icmsValue)));
    }

    [Fact]
    public async Task Icms_complement_refuses_value_above_base()
    {
        var s = await SeedAsync();

        Assert.Equal(K4, await RefusalAsync(s, Icms(s.NonFinancial, icmsBase: 100m, icmsValue: 101m)));
    }

    [Fact]
    public async Task Icms_complement_is_refused_in_sapb1()
    {
        var s = await SeedAsync();

        Assert.Equal(K3, await RefusalAsync(s, Icms(s.NonFinancial), "SAPB1"));
    }

    [Fact]
    public async Task Icms_complement_requires_a_usage()
    {
        var s = await SeedAsync();

        Assert.Equal("Item SOJA: o complemento de ICMS exige a natureza de operação.", await RefusalAsync(s, Icms(null)));
    }

    [Fact]
    public async Task Icms_complement_with_contract_usage_requires_contract()
    {
        var s = await SeedAsync();

        Assert.Equal(RequiresContract("Compra de grãos"), await RefusalAsync(s, Icms(s.ContractUsage)));

        await Create(s.Db).ExecuteAsync(Icms(s.ContractUsage, s.Contract.Key), "ana");
        Assert.Single(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Value_complement_with_other_purchase_usage_and_no_contract_is_saved()
    {
        var s = await SeedAsync();

        await Create(s.Db).ExecuteAsync(Value(s.OtherPurchase, null), "ana");

        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Null(line.PurchaseContractKey);
        Assert.Equal(1_500m, line.ComplementAmount);
    }

    [Fact]
    public async Task Value_complement_with_contract_usage_and_no_contract_is_refused()
    {
        var s = await SeedAsync();

        Assert.Equal(RequiresContract("Compra de grãos"), await RefusalAsync(s, Value(s.ContractUsage, null)));
    }

    [Fact]
    public async Task Value_complement_with_other_purchase_usage_refuses_a_contract()
    {
        var s = await SeedAsync();

        Assert.Equal(
            "A natureza Serviço tomado não aceita contrato de compra: retire o contrato da linha do produto SOJA.",
            await RefusalAsync(s, Value(s.OtherPurchase, s.Contract.Key)));
    }

    [Fact]
    public async Task Value_complement_in_sapb1_still_requires_contract()
    {
        var s = await SeedAsync();

        Assert.Equal("Item SOJA: o complemento de valor exige o contrato de compra na linha.",
            await RefusalAsync(s, Value(s.OtherPurchase, null), "SAPB1"));
    }

    [Fact]
    public async Task Changing_type_to_icms_with_priced_lines_is_refused()
    {
        var s = await SeedAsync();
        var invoice = Document(PurchaseInvoiceType.Normal, s.NonFinancial, quantity: 10m, price: 2m, icmsBase: 0m, icmsValue: 0m);
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();

        var incoming = await s.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        incoming.InvoiceType = PurchaseInvoiceType.IcmsComplement;
        var update = new PurchaseInvoicesUpdateService(s.Db, new FakeBusinessPartnerService(), new FakeItemService(),
            Apply(s.Db, "STANDALONE"));

        var e = await Assert.ThrowsAsync<DefaultException>(() => update.ExecuteAsync(invoice.Key, incoming, "ana"));

        Assert.Equal(K1, e.Message);
    }
}

/// <summary>Spec 2026-10-17 D2: o complemento de ICMS de emissão própria segue as regras C1–C10 do complemento de valor.</summary>
public class PurchaseInvoiceOwnIcmsComplementTests
{
    private static PurchaseInvoice OwnIcms(ValueComplementScenario s, Guid? originalItemKey)
    {
        var invoice = s.Complement((originalItemKey, 0m));
        invoice.InvoiceType = PurchaseInvoiceType.IcmsComplement;
        foreach (var line in invoice.Items)
        {
            line.IcmsBase = 1_000m;
            line.IcmsValue = 120m;
        }
        return invoice;
    }

    private static async Task<string> RefusalAsync(ValueComplementScenario s, PurchaseInvoice invoice) =>
        (await Assert.ThrowsAsync<DefaultException>(() => ValueComplementNfeSeed.Create(s.Db).ExecuteAsync(invoice, "ana"))).Message;

    [Fact]
    public async Task Own_icms_complement_requires_original_item()
    {
        var s = await ValueComplementNfeSeed.SeedAsync();

        Assert.Equal("Item (sem produto): o complemento de ICMS de emissão própria exige o item da nota original.",
            await RefusalAsync(s, OwnIcms(s, null)));
    }

    [Fact]
    public async Task Own_icms_complement_outside_nfe_mode_is_refused_with_its_label()
    {
        var s = await ValueComplementNfeSeed.SeedAsync();
        var branch = await s.Db.Context.Branchs.SingleAsync(b => b.Code == "01");
        branch.IssuesNfe = false;
        await s.Db.SaveChangesAsync();

        Assert.Equal("Complemento de ICMS de emissão própria só existe na filial que emite NF-e pelo Siagro.",
            await RefusalAsync(s, OwnIcms(s, s.OriginalItemKey)));
    }

    [Fact]
    public async Task Own_icms_complement_inherits_from_original()
    {
        var s = await ValueComplementNfeSeed.SeedAsync();
        var invoice = OwnIcms(s, s.OriginalItemKey);

        await ValueComplementNfeSeed.Create(s.Db).ExecuteAsync(invoice, "ana");

        var line = Assert.Single((await s.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .SingleAsync(i => i.Key == invoice.Key)).Items);
        Assert.Equal(s.OriginalItemKey, line.ComplementedItemKey);
        Assert.Equal("TRIGO", line.ItemCode);
        Assert.Equal("TRIGO EM GRAOS", line.ItemName);
        Assert.Equal("KG", line.UnitOfMeasureCode);
        Assert.Equal(s.PurchaseUsage, line.UsageCode);
        Assert.Equal(s.Contract.Key, line.PurchaseContractKey);
        Assert.Equal(0m, line.Quantity);
        Assert.Equal(0m, line.ComplementAmount);
    }

    [Fact]
    public async Task Complemented_item_outside_own_complement_is_refused()
    {
        var s = await ValueComplementNfeSeed.SeedAsync();
        var thirdParty = OwnIcms(s, s.OriginalItemKey);
        thirdParty.IssuerType = DocumentIssuerType.ThirdParty;
        thirdParty.TaxDocumentKind = TaxDocumentKind.Other;
        thirdParty.TaxDocumentNumber = "5001";
        thirdParty.TaxDocumentSeries = "1";
        var line = thirdParty.Items.Single();
        line.ItemCode = "TRIGO";
        line.UsageCode = s.PurchaseUsage;
        line.PurchaseContractKey = s.Contract.Key;

        Assert.Equal("O item da nota original só existe no complemento de emissão própria.", await RefusalAsync(s, thirdParty));
    }
}
