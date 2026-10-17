using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// B7: a unidade de medida é obrigatória na linha do documento de entrada nos QUATRO caminhos que gravam linha
/// (deep-insert, PATCH do cabeçalho, POST e PATCH de linha). Sem ela a devolução copiava a unidade vazia e a NF-e
/// recusava. Complemento de valor é exceção: a linha não tem quantidade.
/// </summary>
public class PurchaseInvoiceUnitOfMeasureTests
{
    private const string Expected = "Informe a unidade de medida da linha 1 (SOJA).";

    private static FakeItemService Catalog() => new(names: new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    private static FakeBusinessPartnerService Partners() => new(names: new Dictionary<string, string> { ["F0001"] = "PRODUTOR" },
            taxIds: new Dictionary<string, string> { ["F0001"] = "52998224725" });

    private static PurchaseInvoice Invoice(string? unit, PurchaseInvoiceType type = PurchaseInvoiceType.Normal)
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F0001", InvoiceType = type };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = type == PurchaseInvoiceType.ValueComplement ? 0m : 10m,
            UnitPrice = 1m, UnitOfMeasureCode = unit,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, Partners(), Catalog(), TaxTestServices.InactivePurchaseApply(db), new FakeDocNumberSequenceService());

    private static async Task<(UnitOfWork Db, PurchaseInvoice Saved)> SeedAsync(string? unit = "KG")
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Invoice(unit);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, invoice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Create_refuses_a_line_without_unit(string? unit)
    {
        var db = TestDb.CreateUnitOfWork();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(Invoice(unit), "tester"));

        Assert.Equal(Expected, e.Message);
        Assert.Empty(await db.Context.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task Create_accepts_a_line_with_unit()
    {
        var db = TestDb.CreateUnitOfWork();

        await Create(db).ExecuteAsync(Invoice("KG"), "tester");

        Assert.Single(await db.Context.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task Create_does_not_require_unit_on_a_value_complement_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Invoice(null, PurchaseInvoiceType.ValueComplement);

        // Outras regras do complemento (contrato, natureza) podem recusar; a da unidade não.
        var e = await Record.ExceptionAsync(() => Create(db).ExecuteAsync(invoice, "tester"));

        Assert.DoesNotContain("unidade de medida", e?.Message ?? "");
    }

    [Fact]
    public async Task Create_does_not_require_unit_on_a_cte_line()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", ShortName = "MATRIZ", TaxId = "12345678000195" });
        await db.Context.SaveChangesAsync();
        var invoice = Invoice(null);
        invoice.BranchCode = "01";
        invoice.TaxDocumentKind = TaxDocumentKind.Cte;
        // O CT-e vale pela chave em toda filial: chave válida do fornecedor F0001 (CPF 52998224725).
        var first43 = "3526" + "08" + "00052998224725" + "57" + "001" + "000000123" + "1" + "00000001";
        invoice.ChaveNFe = first43 + SupplierNfeKeyGuard.CheckDigit(first43);
        invoice.FreightStartState = "SP";
        invoice.FreightEndState = "PR";
        invoice.FreightPayerTaxId = "12345678000195";

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Single(await db.Context.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task Header_update_refuses_a_line_that_loses_its_unit()
    {
        var (db, saved) = await SeedAsync();
        var incoming = Invoice(null);
        incoming.Items.Single().Key = saved.Items.Single().Key;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(db, Partners(), Catalog(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(saved.Key, incoming, "tester"));

        Assert.Equal(Expected, e.Message);
    }

    [Fact]
    public async Task Header_update_refuses_a_new_line_without_unit()
    {
        var (db, saved) = await SeedAsync();
        var incoming = Invoice("KG");
        incoming.Items.Single().Key = saved.Items.Single().Key;
        incoming.AddItem(new PurchaseInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 1m, UnitPrice = 1m });

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(db, Partners(), Catalog(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(saved.Key, incoming, "tester"));

        Assert.Equal("Informe a unidade de medida da linha 2 (SOJA).", e.Message);
    }

    [Fact]
    public async Task Items_create_refuses_a_line_without_unit()
    {
        var (db, saved) = await SeedAsync();
        var line = new PurchaseInvoiceItem { PurchaseInvoiceKey = saved.Key, ItemCode = "SOJA", Quantity = 1m, UnitPrice = 1m };

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, Catalog(), TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(line, "tester"));

        Assert.Equal("Informe a unidade de medida da linha 2 (SOJA).", e.Message);
    }

    [Fact]
    public async Task Items_create_accepts_a_line_with_unit()
    {
        var (db, saved) = await SeedAsync();
        var line = new PurchaseInvoiceItem { PurchaseInvoiceKey = saved.Key, ItemCode = "SOJA", Quantity = 1m, UnitPrice = 1m, UnitOfMeasureCode = "SC" };

        await new PurchaseInvoicesItemsCreateService(db, Catalog(), TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(line, "tester");

        Assert.Equal(2, await db.Context.PurchaseInvoicesItems.CountAsync());
    }

    [Fact]
    public async Task Items_update_refuses_clearing_the_unit()
    {
        var (db, saved) = await SeedAsync();
        var patch = new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m, UnitOfMeasureCode = "" };

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(db, Catalog(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(saved.Items.Single().Key!.Value, patch, "tester"));

        Assert.Equal(Expected, e.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Items_update_does_not_require_unit_on_a_locked_return_or_counter_note(bool nfeReturn)
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Invoice(null, nfeReturn ? PurchaseInvoiceType.Return : PurchaseInvoiceType.Normal);
        invoice.IsNfeReturn = nfeReturn;
        invoice.ProducerInvoiceKey = nfeReturn ? null : Guid.NewGuid();
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var patch = new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 5m, UnitPrice = 1m, UnitOfMeasureCode = "" };

        // Outras regras da devolução podem recusar; a da unidade não (o usuário não consegue editá-la).
        var e = await Record.ExceptionAsync(() =>
            new PurchaseInvoicesItemsUpdateService(db, Catalog(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(invoice.Items.Single().Key!.Value, patch, "tester"));

        Assert.DoesNotContain("unidade de medida", e?.Message ?? "");
    }
}
