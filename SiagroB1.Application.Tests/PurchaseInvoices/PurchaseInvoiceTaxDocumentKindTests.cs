using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Tipo do documento fiscal e autorização da NF-e do fornecedor no cabeçalho (spec terceiro-chave §5). Regra
/// inativa de propósito: aqui só o modelo e a gravação, sem cálculo nem conferência de chave.
/// </summary>
public class PurchaseInvoiceTaxDocumentKindTests
{
    private static PurchaseInvoice Invoice(DocumentIssuerType issuer, TaxDocumentKind kind)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F-001", IssuerType = issuer, TaxDocumentKind = kind,
            SupplierNfeProtocol = "999", SupplierNfeCheckedAt = new DateTime(2020, 1, 1),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db), new FakeDocNumberSequenceService());

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));

    [Fact]
    public void Edm_exposes_the_kind_and_the_supplier_authorization()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var type = builder.GetEdmModel().EntityContainer.FindEntitySet("PurchaseInvoices")!.EntityType;

        Assert.EndsWith("TaxDocumentKind", type.FindProperty("TaxDocumentKind")!.Type.FullName());
        Assert.NotNull(type.FindProperty("SupplierNfeProtocol"));
        Assert.NotNull(type.FindProperty("SupplierNfeCheckedAt"));
    }

    [Fact]
    public async Task Own_entry_is_always_nfe()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Invoice(DocumentIssuerType.Own, TaxDocumentKind.Other);

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(TaxDocumentKind.Nfe, (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).TaxDocumentKind);
    }

    [Fact]
    public async Task Third_party_keeps_the_kind_chosen_and_never_takes_the_authorization_from_the_body()
    {
        var db = TestDb.CreateUnitOfWork();

        await Create(db).ExecuteAsync(Invoice(DocumentIssuerType.ThirdParty, TaxDocumentKind.Other), "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync();
        Assert.Equal((TaxDocumentKind.Other, (string?)null, (DateTime?)null),
            (saved.TaxDocumentKind, saved.SupplierNfeProtocol, saved.SupplierNfeCheckedAt));
    }

    private static readonly DateTime StoredCheckedAt = new(2026, 10, 5, 10, 0, 0);

    private static async Task<PurchaseInvoice> StoredWithAuthorizationAsync(UnitOfWork db)
    {
        await Create(db).ExecuteAsync(Invoice(DocumentIssuerType.ThirdParty, TaxDocumentKind.Nfe), "tester");
        var stored = await db.Context.PurchaseInvoices.SingleAsync();
        (stored.SupplierNfeProtocol, stored.SupplierNfeCheckedAt) = ("135260000000001", StoredCheckedAt);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
    }

    [Fact]
    public async Task Update_changing_the_kind_clears_the_stored_authorization()
    {
        // A consulta da SEFAZ vale para a chave e o tipo de quando foi feita: mudou o tipo, a autorização é velha.
        var db = TestDb.CreateUnitOfWork();
        var changed = await StoredWithAuthorizationAsync(db);
        changed.TaxDocumentKind = TaxDocumentKind.Other;
        (changed.SupplierNfeProtocol, changed.SupplierNfeCheckedAt) = ("FORJADO", DateTime.Now);

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync();
        Assert.Equal((TaxDocumentKind.Other, (string?)null, (DateTime?)null),
            (saved.TaxDocumentKind, saved.SupplierNfeProtocol, saved.SupplierNfeCheckedAt));
    }

    [Fact]
    public async Task Update_changing_the_issuer_type_clears_the_stored_authorization()
    {
        var db = TestDb.CreateUnitOfWork();
        var changed = await StoredWithAuthorizationAsync(db);
        changed.IssuerType = DocumentIssuerType.Own;

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(((string?)null, (DateTime?)null), (saved.SupplierNfeProtocol, saved.SupplierNfeCheckedAt));
    }

    [Fact]
    public async Task Update_that_changes_nothing_relevant_keeps_the_stored_authorization()
    {
        var db = TestDb.CreateUnitOfWork();
        var changed = await StoredWithAuthorizationAsync(db);
        changed.Comments = "conferido";
        (changed.SupplierNfeProtocol, changed.SupplierNfeCheckedAt) = ("FORJADO", null);

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(("conferido", "135260000000001", (DateTime?)StoredCheckedAt),
            (saved.Comments, saved.SupplierNfeProtocol, saved.SupplierNfeCheckedAt));
    }

    [Fact]
    public async Task Update_turns_an_own_entry_back_to_nfe()
    {
        var db = TestDb.CreateUnitOfWork();
        await Create(db).ExecuteAsync(Invoice(DocumentIssuerType.Own, TaxDocumentKind.Nfe), "tester");
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.TaxDocumentKind = TaxDocumentKind.Other;

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        Assert.Equal(TaxDocumentKind.Nfe, (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).TaxDocumentKind);
    }
}
