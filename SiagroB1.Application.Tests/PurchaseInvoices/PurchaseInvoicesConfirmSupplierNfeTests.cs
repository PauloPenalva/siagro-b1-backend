using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Confirmar o documento eletrônico de terceiro: chave conferida sempre; com a filial em Produção, a NF-e do fornecedor
/// consultada na SEFAZ da UF da chave e só autorizada confirma (spec terceiro-chave §8).
/// </summary>
public class PurchaseInvoicesConfirmSupplierNfeTests
{
    private const string PrKey = "41261000052998224725550010000004561123456780";

    private static async Task<(UnitOfWork Db, PurchaseInvoice Doc)> SeedAsync(
        NfeEnvironment environment = NfeEnvironment.Production, string? key = SupplierNfeXml.AccessKey,
        TaxDocumentKind kind = TaxDocumentKind.Nfe, PurchaseInvoiceType type = PurchaseInvoiceType.Normal)
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        (await db.Context.BranchNfeSettings.SingleAsync(s => s.BranchCode == "01")).Environment = environment;
        var doc = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = type, InvoiceStatus = InvoiceStatus.Pending,
            TaxDocumentKind = kind, ChaveNFe = key, TaxDocumentNumber = "456", TaxDocumentSeries = "1",
        };
        doc.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        });
        db.Context.PurchaseInvoices.Add(doc);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, doc);
    }

    private static Task ConfirmAsync(UnitOfWork db, PurchaseInvoice doc, FakeNfeSefazClient sefaz, string erp = "STANDALONE") =>
        TaxTestServices.PurchaseConfirm(db, erp, PurchaseNfeTestSeed.Partners(), sefaz).ExecuteAsync(doc.Key, "tester");

    private static Task<PurchaseInvoice> LoadAsync(UnitOfWork db, PurchaseInvoice doc) =>
        db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == doc.Key);

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public async Task Authorized_key_confirms_and_stores_the_protocol(int status)
    {
        var (db, doc) = await SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key, status));

        await ConfirmAsync(db, doc, sefaz);

        var saved = await LoadAsync(db, doc);
        Assert.Equal((InvoiceStatus.Confirmed, "135260000000001"), (saved.InvoiceStatus, saved.SupplierNfeProtocol));
        Assert.NotNull(saved.SupplierNfeCheckedAt);
        Assert.Equal(new[] { SupplierNfeXml.AccessKey }, sefaz.Consulted);
        Assert.Equal("SP", sefaz.ConsultedSettings.Single().IssuerState);
    }

    [Theory]
    [InlineData(101, "A NF-e do fornecedor está cancelada na SEFAZ.")]
    [InlineData(151, "A NF-e do fornecedor está cancelada na SEFAZ.")]
    [InlineData(155, "A NF-e do fornecedor está cancelada na SEFAZ.")]
    [InlineData(110, "A NF-e do fornecedor teve o uso denegado na SEFAZ.")]
    [InlineData(301, "A NF-e do fornecedor teve o uso denegado na SEFAZ.")]
    [InlineData(217, "A chave de acesso não consta na SEFAZ.")]
    [InlineData(999, "A SEFAZ não confirmou a NF-e do fornecedor (999 – Rejeição: teste). Tente novamente.")]
    public async Task Unauthorized_key_is_refused_and_the_document_stays_pending(int status, string message)
    {
        var (db, doc) = await SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(status, "Rejeição: teste"));

        var e = await Assert.ThrowsAsync<DefaultException>(() => ConfirmAsync(db, doc, sefaz));

        Assert.Equal(message, e.Message);
        var saved = await LoadAsync(db, doc);
        Assert.Equal((InvoiceStatus.Pending, (string?)null), (saved.InvoiceStatus, saved.SupplierNfeProtocol));
    }

    [Fact]
    public async Task No_answer_from_sefaz_is_refused()
    {
        var (db, doc) = await SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(FakeNfeSefazClient.NoResponse);

        var e = await Assert.ThrowsAsync<DefaultException>(() => ConfirmAsync(db, doc, sefaz));

        Assert.Equal("A SEFAZ não respondeu. Tente novamente.", e.Message);
        Assert.Equal(InvoiceStatus.Pending, (await LoadAsync(db, doc)).InvoiceStatus);
    }

    [Fact]
    public async Task Key_from_another_state_is_consulted_at_that_state()
    {
        var (db, doc) = await SeedAsync(key: PrKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await ConfirmAsync(db, doc, sefaz);

        Assert.Equal("PR", sefaz.ConsultedSettings.Single().IssuerState);
    }

    [Fact]
    public async Task Homologation_does_not_consult()
    {
        var (db, doc) = await SeedAsync(NfeEnvironment.Homologation);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz);

        var saved = await LoadAsync(db, doc);
        Assert.Equal((InvoiceStatus.Confirmed, (string?)null), (saved.InvoiceStatus, saved.SupplierNfeProtocol));
        Assert.Empty(sefaz.Consulted);
    }

    [Theory]
    [InlineData(NfeEnvironment.Homologation)]
    [InlineData(NfeEnvironment.Production)]
    public async Task Electronic_document_without_key_is_refused_in_both_environments(NfeEnvironment environment)
    {
        var (db, doc) = await SeedAsync(environment, key: null);

        var e = await Assert.ThrowsAsync<DefaultException>(() => ConfirmAsync(db, doc, new FakeNfeSefazClient()));

        Assert.Equal("Informe a chave de acesso da NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public async Task Other_document_confirms_without_key_and_without_consulting()
    {
        var (db, doc) = await SeedAsync(key: null, kind: TaxDocumentKind.Other);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz);

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, doc)).InvoiceStatus);
        Assert.Empty(sefaz.Consulted);
    }

    [Fact]
    public async Task Customer_return_confirms_without_consulting()
    {
        var (db, doc) = await SeedAsync(key: null, type: PurchaseInvoiceType.Return);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz);

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, doc)).InvoiceStatus);
        Assert.Empty(sefaz.Consulted);
    }

    [Fact]
    public async Task Branch_without_the_rule_confirms_as_before()
    {
        var (db, doc) = await SeedAsync(key: null);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz, "SAPB1");

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, doc)).InvoiceStatus);
        Assert.Empty(sefaz.Consulted);
    }
}
