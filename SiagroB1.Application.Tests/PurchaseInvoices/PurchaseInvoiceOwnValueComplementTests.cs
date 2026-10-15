using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-14, regras 1–5 e C7: a linha do complemento de valor de emissão própria.</summary>
public class PurchaseInvoiceOwnValueComplementTests
{
    private const string NotIssuable =
        "Item TRIGO: a nota original DE000005 precisa ser uma entrada própria normal, com NF-e autorizada e não cancelada.";
    private const string OtherParty = "Item TRIGO: a nota original DE000005 é de outro produtor ou de outra filial.";
    private const string Duplicated = "O item DE000005/1 já está complementado nesta nota.";
    private const string Inherited =
        "Item TRIGO: produto, unidade e natureza vêm do item da nota original e não podem ser alterados.";
    private const string InheritedContract = "Item TRIGO: o contrato vem do item da nota original e não pode ser alterado.";
    private const string OnlyOwnComplement = "O item da nota original só existe no complemento de valor de emissão própria.";

    /// <summary>D5: a original sem contrato na linha — o caso real de toda entrada própria autorizada da CEAGUI dev.</summary>
    private static Task WithoutContractAsync(ValueComplementScenario s) =>
        ChangeAsync(s.Db, s.OriginalKey, o => o.Items.Single().PurchaseContractKey = null);

    /// <summary>O contrato que o operador escolheu na tela, em todas as linhas do rascunho.</summary>
    private static PurchaseInvoice WithContract(PurchaseInvoice invoice, Guid? contractKey)
    {
        foreach (var line in invoice.Items)
            line.PurchaseContractKey = contractKey;
        return invoice;
    }

    private static PurchaseInvoicesTaxApplyService Apply(ValueComplementScenario s) =>
        TaxTestServices.PurchaseApply(s.Db, PurchaseNfeTestSeed.Partners());

    private static PurchaseInvoicesItemsCreateService ItemsCreate(ValueComplementScenario s) => new(s.Db, new FakeItemService(), Apply(s));

    private static PurchaseInvoicesItemsUpdateService ItemsUpdate(ValueComplementScenario s) => new(s.Db, new FakeItemService(), Apply(s));

    private static PurchaseInvoicesUpdateService Update(ValueComplementScenario s) =>
        new(s.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(s));

    private static async Task<string> RefusalAsync(ValueComplementScenario s, PurchaseInvoice invoice) =>
        (await Assert.ThrowsAsync<DefaultException>(() => Create(s.Db).ExecuteAsync(invoice, "ana"))).Message;

    /// <summary>Complemento gravado (Pendente) e o rastreador limpo, como entre duas requisições.</summary>
    private static async Task<PurchaseInvoice> SavedComplementAsync(ValueComplementScenario s, decimal amount = 300m)
    {
        var invoice = s.Complement((s.OriginalItemKey, amount));
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();
        return invoice;
    }

    private static Task<PurchaseInvoice> ReloadAsync(ValueComplementScenario s, Guid key) =>
        s.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == key);

    [Fact]
    public async Task Line_inherits_product_unit_nature_and_contract_from_the_original()
    {
        var s = await SeedAsync();

        var invoice = await SavedComplementAsync(s);

        var line = Assert.Single((await ReloadAsync(s, invoice.Key)).Items);
        Assert.Equal(s.OriginalItemKey, line.ComplementedItemKey);
        Assert.Equal("TRIGO", line.ItemCode);
        Assert.Equal("TRIGO EM GRAOS", line.ItemName);
        Assert.Equal("KG", line.UnitOfMeasureCode);
        Assert.Equal(s.PurchaseUsage, line.UsageCode);
        Assert.Equal(s.Contract.Key, line.PurchaseContractKey);
        Assert.Equal(0m, line.Quantity);
        Assert.Equal(0m, line.UnitPrice);
        Assert.Equal(300m, line.ComplementAmount);
    }

    [Fact]
    public async Task Line_without_the_original_item_is_refused()
    {
        var s = await SeedAsync();

        Assert.Equal("Item (sem produto): o complemento de valor de emissão própria exige o item da nota original.",
            await RefusalAsync(s, s.Complement((null, 300m))));
        Assert.Equal("Item (sem produto): o complemento de valor de emissão própria exige o item da nota original.",
            await RefusalAsync(s, s.Complement((Guid.NewGuid(), 300m))));
        Assert.Empty(s.Db.Context.PurchaseInvoices.AsNoTracking().Where(i => i.InvoiceType == PurchaseInvoiceType.ValueComplement));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("third-party")]
    [InlineData("nfe-cancelled")]
    [InlineData("document-cancelled")]
    [InlineData("complement")]
    [InlineData("return")]
    public async Task Original_must_be_an_authorized_own_normal_entry(string variant)
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.OriginalKey, original =>
        {
            switch (variant)
            {
                case "pending": original.InvoiceStatus = InvoiceStatus.Pending; original.NfeStatus = NfeStatus.None; break;
                case "third-party": original.IssuerType = DocumentIssuerType.ThirdParty; break;
                case "nfe-cancelled": original.NfeStatus = NfeStatus.Cancelled; break;
                case "document-cancelled": original.InvoiceStatus = InvoiceStatus.Cancelled; break;
                case "complement": original.InvoiceType = PurchaseInvoiceType.ValueComplement; break;
                case "return": original.InvoiceType = PurchaseInvoiceType.Return; break;
            }
        });

        Assert.Equal(NotIssuable, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));
    }

    [Fact]
    public async Task Original_of_another_supplier_or_branch_is_refused()
    {
        var s = await SeedAsync();

        await ChangeAsync(s.Db, s.OriginalKey, o => o.CardCode = "F-OUTRO");
        Assert.Equal(OtherParty, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));

        await ChangeAsync(s.Db, s.OriginalKey, o => { o.CardCode = PurchaseNfeTestSeed.Supplier; o.BranchCode = "02"; });
        Assert.Equal(OtherParty, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));
    }

    [Fact]
    public async Task Same_original_item_twice_in_one_note_is_refused()
    {
        var s = await SeedAsync();

        // Deep-insert com as duas linhas.
        Assert.Equal(Duplicated, await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m), (s.OriginalItemKey, 100m))));

        // Linha a linha (grade do Editar → POST de linha).
        var invoice = await SavedComplementAsync(s);
        var extra = new PurchaseInvoiceItem
        {
            Key = NextLineKey(), PurchaseInvoiceKey = invoice.Key, ComplementedItemKey = s.OriginalItemKey, ComplementAmount = 50m,
        };
        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemsCreate(s).ExecuteAsync(extra, "ana"));
        Assert.Equal(Duplicated, e.Message);
    }

    [Fact]
    public async Task Successive_complements_of_the_same_item_are_allowed()
    {
        var s = await SeedAsync();

        await SavedComplementAsync(s, 300m);
        await SavedComplementAsync(s, 120m);

        Assert.Equal(2, await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().CountAsync(l => l.ComplementedItemKey == s.OriginalItemKey));
    }

    [Fact]
    public async Task Inherited_fields_cannot_be_changed_on_line_update()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.UsageCode = s.Purchase.ReturnUsage;

        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana"));

        Assert.Equal(Inherited, e.Message);
    }

    [Fact]
    public async Task Inherited_fields_cannot_be_changed_on_header_update()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.Items.Single().UnitOfMeasureCode = "SC";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        Assert.Equal(Inherited, e.Message);
    }

    [Fact]
    public async Task Only_the_complement_amount_changes_on_line_update()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.ComplementAmount = 450m;

        await ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana");

        Assert.Equal(450m, (await ReloadAsync(s, invoice.Key)).Items.Single().ComplementAmount);
    }

    [Fact]
    public async Task Changing_the_supplier_after_including_lines_is_refused()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.CardCode = "F-OUTRO";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        Assert.Equal(OtherParty, e.Message);
    }

    [Fact]
    public async Task Changing_the_branch_after_including_lines_is_refused()
    {
        var s = await SeedAsync();
        var invoice = await SavedComplementAsync(s);
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.BranchCode = "02";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        Assert.Equal(OtherParty, e.Message);
    }

    [Fact]
    public async Task Contract_sent_for_an_original_with_contract_is_replaced_by_the_inherited_one()
    {
        var s = await SeedAsync();
        var other = await AddContractAsync(s.Db, "PC-TRIGO-2");

        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), other.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");

        Assert.Equal(s.Contract.Key, (await ReloadAsync(s, invoice.Key)).Items.Single().PurchaseContractKey);
    }

    [Fact]
    public async Task Inherited_contract_cannot_be_changed_on_update()
    {
        var s = await SeedAsync();
        var other = await AddContractAsync(s.Db, "PC-TRIGO-2");
        var invoice = await SavedComplementAsync(s);
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.PurchaseContractKey = other.Key;

        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana"));

        Assert.Equal(InheritedContract, e.Message);
    }

    [Fact]
    public async Task Original_without_contract_takes_the_contract_chosen_by_the_operator()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var chosen = await AddContractAsync(s.Db, "PC-TRIGO-2");

        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), chosen.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");

        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        Assert.Equal(chosen.Key, line.PurchaseContractKey);
        Assert.Equal("TRIGO", line.ItemCode);
    }

    [Fact]
    public async Task Original_without_contract_still_requires_a_contract_on_the_line()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);

        Assert.Equal("Item TRIGO: o complemento de valor exige o contrato de compra na linha.",
            await RefusalAsync(s, s.Complement((s.OriginalItemKey, 300m))));
    }

    [Fact]
    public async Task Chosen_contract_goes_through_the_contract_rules()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var otherSupplier = await AddContractAsync(s.Db, "PC-OUTRO", cardCode: "F-OUTRO");
        var draft = await AddContractAsync(s.Db, "PC-RASCUNHO", status: ContractStatus.Draft);

        Assert.Equal(
            "O contrato PC-OUTRO, amarrado na linha do produto TRIGO, é de outro fornecedor e não pode ser amarrado a este documento.",
            await RefusalAsync(s, WithContract(s.Complement((s.OriginalItemKey, 300m)), otherSupplier.Key)));
        Assert.Equal(
            "O contrato PC-RASCUNHO, amarrado na linha do produto TRIGO, não está aprovado nem encerrado e não pode ser amarrado a um documento de entrada.",
            await RefusalAsync(s, WithContract(s.Complement((s.OriginalItemKey, 300m)), draft.Key)));
    }

    [Fact]
    public async Task Chosen_contract_can_be_changed_on_update()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var first = await AddContractAsync(s.Db, "PC-TRIGO-2");
        var second = await AddContractAsync(s.Db, "PC-TRIGO-3");
        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), first.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();
        var line = (await ReloadAsync(s, invoice.Key)).Items.Single();
        line.PurchaseContractKey = second.Key;

        await ItemsUpdate(s).ExecuteAsync(line.Key!.Value, line, "ana");

        Assert.Equal(second.Key, (await ReloadAsync(s, invoice.Key)).Items.Single().PurchaseContractKey);
    }

    [Fact]
    public async Task Changing_the_supplier_with_a_chosen_contract_is_refused_by_C3()
    {
        var s = await SeedAsync();
        await WithoutContractAsync(s);
        var chosen = await AddContractAsync(s.Db, "PC-TRIGO-2");
        var invoice = WithContract(s.Complement((s.OriginalItemKey, 300m)), chosen.Key);
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        s.Db.Context.ChangeTracker.Clear();
        var incoming = await ReloadAsync(s, invoice.Key);
        incoming.CardCode = "F-OUTRO";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(s).ExecuteAsync(invoice.Key, incoming, "ana"));

        // C3 primeiro, não "é de outro fornecedor" do contrato: a guarda de contrato roda depois de C3.
        Assert.Equal(OtherParty, e.Message);
    }

    [Fact]
    public async Task Original_item_key_only_exists_in_the_own_complement()
    {
        var s = await SeedAsync();

        var normal = s.Complement((s.OriginalItemKey, 0m));
        normal.InvoiceType = PurchaseInvoiceType.Normal;
        var normalLine = normal.Items.Single();
        normalLine.ItemCode = "TRIGO";
        normalLine.UnitOfMeasureCode = "KG";
        normalLine.Quantity = 10m;
        normalLine.UnitPrice = 1.5m;
        normalLine.UsageCode = s.PurchaseUsage;
        Assert.Equal(OnlyOwnComplement, await RefusalAsync(s, normal));

        var thirdParty = s.Complement((s.OriginalItemKey, 300m));
        thirdParty.IssuerType = DocumentIssuerType.ThirdParty;
        thirdParty.TaxDocumentKind = TaxDocumentKind.Other;
        thirdParty.TaxDocumentNumber = "5001";
        thirdParty.TaxDocumentSeries = "1";
        var thirdPartyLine = thirdParty.Items.Single();
        thirdPartyLine.ItemCode = "TRIGO";
        thirdPartyLine.UsageCode = s.PurchaseUsage;
        thirdPartyLine.PurchaseContractKey = s.Contract.Key;
        Assert.Equal(OnlyOwnComplement, await RefusalAsync(s, thirdParty));
    }
}
