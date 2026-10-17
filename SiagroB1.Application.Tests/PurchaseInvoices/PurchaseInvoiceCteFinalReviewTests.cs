using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Ajustes da revisão final do CT-e de frete tomado (spec 2026-10-17): chave fora da regra fiscal, vínculos e tipo.</summary>
public class PurchaseInvoiceCteFinalReviewTests
{
    private static string CteKey()
    {
        var first43 = "3526" + "10" + "00052998224725" + "57" + "001" + "000000456" + "1" + "12345678";
        return first43 + SupplierNfeKeyGuard.CheckDigit(first43);
    }

    private static PurchaseInvoice Cte(string? key, Guid? invoiceKey = null)
    {
        var invoice = new PurchaseInvoice
        {
            Key = invoiceKey ?? Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Cte, ChaveNFe = key, TaxDocumentNumber = "456", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 1), FreightStartState = "SP", FreightEndState = "PR",
            FreightPayerTaxId = "12345678000195", CteIcmsCst = "00", CteIcmsValue = 10m,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 500m,
        });
        return invoice;
    }

    /// <summary>Filial sem a regra fiscal ativa (ambiente SAPB1): o guard da chave só rodava com a regra ativa.</summary>
    private static (UnitOfWork Db, PurchaseInvoicesCreateService Create, PurchaseInvoicesUpdateService Update) Services()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", ShortName = "MATRIZ", TaxId = "12.345.678/0001-95", IssuesNfe = false,
        });
        db.Context.SaveChanges();
        var partners = PurchaseNfeTestSeed.Partners();
        var apply = TaxTestServices.InactivePurchaseApply(db);
        return (db,
            new PurchaseInvoicesCreateService(db, partners, new FakeItemService(), apply, new FakeDocNumberSequenceService()),
            new PurchaseInvoicesUpdateService(db, partners, new FakeItemService(), apply));
    }

    [Fact]
    public async Task Cte_without_key_is_rejected_in_branch_without_tax_rule()
    {
        var (_, create, _) = Services();

        var e = await Assert.ThrowsAsync<DefaultException>(() => create.ExecuteAsync(Cte(null), "ana"));

        Assert.Equal("Informe a chave de acesso do CT-e da transportadora.", e.Message);
    }

    [Fact]
    public async Task Cte_key_with_spaces_is_normalized_in_branch_without_tax_rule()
    {
        var (db, create, _) = Services();
        var spaced = string.Join(' ', Enumerable.Range(0, 11).Select(i => CteKey().Substring(i * 4, 4)));
        var invoice = Cte(spaced);

        await create.ExecuteAsync(invoice, "ana");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == invoice.Key);
        Assert.Equal(CteKey(), saved.ChaveNFe);
    }

    [Fact]
    public async Task Duplicate_cte_key_with_spaces_is_rejected()
    {
        var (_, create, _) = Services();
        await create.ExecuteAsync(Cte(CteKey()), "ana");
        var spaced = string.Join(' ', Enumerable.Range(0, 11).Select(i => CteKey().Substring(i * 4, 4)));

        var e = await Assert.ThrowsAsync<DefaultException>(() => create.ExecuteAsync(Cte(spaced), "ana"));

        Assert.Equal($"Já existe documento de entrada com a chave de acesso {CteKey()}.", e.Message);
    }

    [Fact]
    public async Task Update_cte_without_key_is_rejected_in_branch_without_tax_rule()
    {
        var (db, create, update) = Services();
        var saved = Cte(CteKey());
        await create.ExecuteAsync(saved, "ana");
        db.Context.ChangeTracker.Clear();

        var body = Cte(null, saved.Key);
        body.Items.Single().Key = saved.Items.Single().Key;
        var e = await Assert.ThrowsAsync<DefaultException>(() => update.ExecuteAsync(saved.Key, body, "ana"));

        Assert.Equal("Informe a chave de acesso do CT-e da transportadora.", e.Message);
    }

    [Fact]
    public async Task Create_discards_transported_documents_from_the_body()
    {
        var (db, create, _) = Services();
        var invoice = Cte(CteKey());
        invoice.TransportedDocuments.Add(new PurchaseInvoiceTransportedDocument
        {
            Key = Guid.NewGuid(), AccessKey = "35261000000000000000550010000000011000000017",
        });

        await create.ExecuteAsync(invoice, "ana");

        Assert.Empty(await db.Context.PurchaseInvoiceTransportedDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Changing_kind_away_from_cte_with_links_is_rejected()
    {
        var (db, create, update) = Services();
        var saved = Cte(CteKey());
        await create.ExecuteAsync(saved, "ana");
        db.Context.PurchaseInvoiceTransportedDocuments.Add(new PurchaseInvoiceTransportedDocument
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = saved.Key, AccessKey = "35261000000000000000550010000000011000000017",
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var body = Cte(CteKey(), saved.Key);
        body.Items.Single().Key = saved.Items.Single().Key;
        body.TaxDocumentKind = TaxDocumentKind.Other;
        var e = await Assert.ThrowsAsync<DefaultException>(() => update.ExecuteAsync(saved.Key, body, "ana"));

        Assert.Equal("Remova os vínculos de NF-e Referenciada(s) antes de mudar o tipo do documento.", e.Message);
    }

    [Fact]
    public async Task Changing_kind_away_from_cte_without_links_clears_cte_icms()
    {
        var (db, create, update) = Services();
        var saved = Cte(CteKey());
        await create.ExecuteAsync(saved, "ana");
        db.Context.ChangeTracker.Clear();

        var body = Cte(null, saved.Key);
        body.Items.Single().Key = saved.Items.Single().Key;
        body.TaxDocumentKind = TaxDocumentKind.Other;
        await update.ExecuteAsync(saved.Key, body, "ana");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == saved.Key);
        Assert.Equal(TaxDocumentKind.Other, reloaded.TaxDocumentKind);
        Assert.Null(reloaded.CteIcmsCst);
        Assert.Null(reloaded.CteIcmsValue);
    }
}
