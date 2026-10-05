using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Conferência local da chave da NF-e do fornecedor (spec terceiro-chave §7), nos dois ambientes.</summary>
public class SupplierNfeKeyGuardTests
{
    private const string Cpf = "52998224725";

    private static PurchaseInvoice Doc(string? key, string? number = "456", string? series = "1") => new()
    {
        Key = Guid.NewGuid(), CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.ThirdParty,
        InvoiceType = PurchaseInvoiceType.Normal, TaxDocumentKind = TaxDocumentKind.Nfe, ChaveNFe = key,
        TaxDocumentNumber = number, TaxDocumentSeries = series,
    };

    private static string Refusal(PurchaseInvoice doc, string? taxId = Cpf) =>
        Assert.Throws<DefaultException>(() => SupplierNfeKeyGuard.Ensure(doc, taxId)).Message;

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Missing_key_is_refused(string? key) =>
        Assert.Equal("Informe a chave de acesso da NF-e do fornecedor.", Refusal(Doc(key)));

    [Theory]
    [InlineData("3526100005299822472555001000000456112345678")]
    [InlineData("3526100005299822472555001000000456112345678A")]
    public void Key_that_is_not_44_digits_is_refused(string key) =>
        Assert.Equal("A chave de acesso tem 44 dígitos.", Refusal(Doc(key)));

    [Fact]
    public void Wrong_check_digit_is_refused() =>
        Assert.Equal("Chave de acesso inválida: o dígito verificador não confere.",
            Refusal(Doc("35261000052998224725550010000004561123456781")));

    [Fact]
    public void Nfce_key_is_refused() =>
        Assert.Equal("A chave não é de NF-e (modelo 55).",
            Refusal(Doc("35261011222333000181650010000007891876543214", "789"), "11222333000181"));

    [Fact]
    public void Key_of_another_issuer_is_refused() =>
        Assert.Equal("A chave é de outro emitente: o CNPJ/CPF não é o do fornecedor.",
            Refusal(Doc(SupplierNfeXml.AccessKey), "11222333000181"));

    [Fact]
    public void Supplier_without_tax_id_is_refused_as_another_issuer() =>
        Assert.Equal("A chave é de outro emitente: o CNPJ/CPF não é o do fornecedor.", Refusal(Doc(SupplierNfeXml.AccessKey), null));

    [Theory]
    [InlineData("457", "1")]
    [InlineData("456", "2")]
    public void Number_or_series_different_from_the_key_is_refused(string number, string series) =>
        Assert.Equal("O número/série da chave não conferem com os do documento.", Refusal(Doc(SupplierNfeXml.AccessKey, number, series)));

    [Fact]
    public void Cpf_supplier_key_passes()
    {
        // Review Focus 1: produtor rural pessoa física — o CPF vem com três zeros à esquerda na chave.
        SupplierNfeKeyGuard.Ensure(Doc(SupplierNfeXml.AccessKey), "529.982.247-25");
    }

    [Fact]
    public void Cnpj_supplier_key_passes() =>
        SupplierNfeKeyGuard.Ensure(Doc("35261011222333000181550010000007891876543211", "789"), "11.222.333/0001-81");

    [Fact]
    public void Key_pasted_with_spaces_is_normalized()
    {
        // Review Focus 2: o DANFE imprime a chave em grupos de 4.
        var doc = Doc("3526 1000 0529 9822 4725 5500 1000 0004 5611 2345 6782");

        SupplierNfeKeyGuard.Ensure(doc, Cpf);

        Assert.Equal(SupplierNfeXml.AccessKey, doc.ChaveNFe);
    }

    [Fact]
    public void Number_and_series_with_leading_zeros_pass()
    {
        // Review Focus 3.
        SupplierNfeKeyGuard.Ensure(Doc(SupplierNfeXml.AccessKey, "000000456", "001"), Cpf);
    }

    [Fact]
    public void Blank_number_and_series_are_filled_from_the_key_without_leading_zeros()
    {
        var doc = Doc(SupplierNfeXml.AccessKey, null, " ");

        SupplierNfeKeyGuard.Ensure(doc, Cpf);

        Assert.Equal(("456", "1"), (doc.TaxDocumentNumber, doc.TaxDocumentSeries));
    }

    [Theory]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, TaxDocumentKind.Nfe, true)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, TaxDocumentKind.Other, false)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Return, TaxDocumentKind.Nfe, false)]
    [InlineData(DocumentIssuerType.Own, PurchaseInvoiceType.Normal, TaxDocumentKind.Nfe, false)]
    public void Applies_only_to_the_third_party_normal_electronic_document(
        DocumentIssuerType issuer, PurchaseInvoiceType type, TaxDocumentKind kind, bool expected)
    {
        var doc = Doc(null);
        (doc.IssuerType, doc.InvoiceType, doc.TaxDocumentKind) = (issuer, type, kind);

        Assert.Equal(expected, SupplierNfeKeyGuard.AppliesTo(doc));
    }

    // --- gravação ---

    private static PurchaseInvoice Saved(string? key, TaxDocumentKind kind = TaxDocumentKind.Nfe)
    {
        var doc = Doc(key);
        doc.TaxDocumentKind = kind;
        doc.BranchCode = "01";
        doc.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        });
        return doc;
    }

    private static async Task<(UnitOfWork Db, int Usage)> ActiveAsync()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        return (db, await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners(), erp));

    [Fact]
    public async Task Create_refuses_a_key_with_a_wrong_check_digit()
    {
        var (db, usage) = await ActiveAsync();
        var doc = Saved("35261000052998224725550010000004561123456781");
        doc.Items.Single().UsageCode = usage;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(doc, "tester"));

        Assert.Equal("Chave de acesso inválida: o dígito verificador não confere.", e.Message);
    }

    [Fact]
    public async Task Other_document_saves_without_a_key()
    {
        var (db, usage) = await ActiveAsync();
        var doc = Saved(null, TaxDocumentKind.Other);
        doc.Items.Single().UsageCode = usage;

        await Create(db).ExecuteAsync(doc, "tester");

        Assert.True(await db.Context.PurchaseInvoices.AnyAsync(i => i.Key == doc.Key));
    }

    [Fact]
    public async Task Branch_without_the_rule_saves_any_key()
    {
        var (db, _) = await ActiveAsync();

        await Create(db, "SAPB1").ExecuteAsync(Saved("123"), "tester");

        Assert.Equal("123", (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.ChaveNFe == "123")).ChaveNFe);
    }

    [Fact]
    public async Task Update_refuses_a_key_already_used_by_another_document()
    {
        var (db, usage) = await ActiveAsync();
        var first = Saved(SupplierNfeXml.AccessKey);
        first.Items.Single().UsageCode = usage;
        await Create(db).ExecuteAsync(first, "tester");
        var second = Saved(null, TaxDocumentKind.Other);
        second.Items.Single().UsageCode = usage;
        await Create(db).ExecuteAsync(second, "tester");
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == second.Key);
        changed.TaxDocumentKind = TaxDocumentKind.Nfe;
        changed.ChaveNFe = SupplierNfeXml.AccessKey;

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesUpdateService(db, PurchaseNfeTestSeed.Partners(),
            new FakeItemService(), TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners())).ExecuteAsync(changed.Key, changed, "tester"));

        Assert.Equal($"Já existe documento de entrada com a chave de NF-e {SupplierNfeXml.AccessKey}.", e.Message);
    }

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()));

    private static async Task<PurchaseInvoice> StoredAsync(UnitOfWork db, int usage)
    {
        var doc = Saved(SupplierNfeXml.AccessKey);
        doc.Items.Single().UsageCode = usage;
        await Create(db).ExecuteAsync(doc, "tester");
        db.Context.ChangeTracker.Clear();
        return await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == doc.Key);
    }

    [Fact]
    public async Task Create_stores_the_normalized_key()
    {
        var (db, usage) = await ActiveAsync();
        var doc = Saved("3526 1000 0529 9822 4725 5500 1000 0004 5611 2345 6782");
        doc.Items.Single().UsageCode = usage;

        await Create(db).ExecuteAsync(doc, "tester");

        Assert.Equal(SupplierNfeXml.AccessKey, (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == doc.Key)).ChaveNFe);
    }

    [Fact]
    public async Task Update_refuses_a_key_with_a_wrong_check_digit()
    {
        var (db, usage) = await ActiveAsync();
        var changed = await StoredAsync(db, usage);
        changed.ChaveNFe = "35261000052998224725550010000004561123456781";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(changed.Key, changed, "tester"));

        Assert.Equal("Chave de acesso inválida: o dígito verificador não confere.", e.Message);
    }

    [Fact]
    public async Task Update_changing_the_key_clears_the_stored_authorization()
    {
        var (db, usage) = await ActiveAsync();
        var changed = await StoredAsync(db, usage);
        var stored = await db.Context.PurchaseInvoices.SingleAsync(i => i.Key == changed.Key);
        (stored.SupplierNfeProtocol, stored.SupplierNfeCheckedAt) = ("135260000000001", new DateTime(2026, 10, 5, 10, 0, 0));
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        // Chave de outro número/série do mesmo fornecedor: o documento passa a apontar para outra NF-e.
        changed.ChaveNFe = "35261000052998224725550010000004571123456780";
        changed.TaxDocumentNumber = "457";

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == changed.Key);
        Assert.Equal(("35261000052998224725550010000004571123456780", (string?)null, (DateTime?)null),
            (saved.ChaveNFe, saved.SupplierNfeProtocol, saved.SupplierNfeCheckedAt));
    }

    [Fact]
    public async Task Update_resending_the_key_with_spaces_keeps_the_stored_authorization()
    {
        var (db, usage) = await ActiveAsync();
        var changed = await StoredAsync(db, usage);
        var stored = await db.Context.PurchaseInvoices.SingleAsync(i => i.Key == changed.Key);
        (stored.SupplierNfeProtocol, stored.SupplierNfeCheckedAt) = ("135260000000001", new DateTime(2026, 10, 5, 10, 0, 0));
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        changed.ChaveNFe = "3526 1000 0529 9822 4725 5500 1000 0004 5611 2345 6782";

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == changed.Key);
        Assert.Equal((SupplierNfeXml.AccessKey, "135260000000001"), (saved.ChaveNFe, saved.SupplierNfeProtocol));
    }
}
