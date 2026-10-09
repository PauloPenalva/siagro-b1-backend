using SiagroB1.Application.Services.Nfe;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Travas da NF-e no Documento de Entrada (spec §7) — todas pelo NfeStatus, que fica None na Yokotobi e na MH Agro.</summary>
public class PurchaseInvoiceNfeLockTests
{
    private static async Task<(UnitOfWork Db, PurchaseInvoice Invoice)> SeedAsync(
        NfeStatus nfeStatus, InvoiceStatus status = InvoiceStatus.Pending, bool nfeReturn = false, bool issuesNfe = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = "12345678000195", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        // A devolução de compra aponta uma entrada própria autorizada (o saldo da edição é conferido contra ela).
        PurchaseInvoice? origin = null;
        if (nfeReturn)
        {
            origin = new PurchaseInvoice
            {
                Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F-SP", IssuerType = DocumentIssuerType.Own,
                InvoiceType = PurchaseInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed, NfeStatus = NfeStatus.Authorized,
                ChaveNFe = "35261012345678000195550010000001231481516234", TaxDocumentNumber = "000000001", TaxDocumentSeries = "9",
            };
            origin.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m, NfeItemNumber = 1,
            });
            db.Context.PurchaseInvoices.Add(origin);
        }

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F-SP", IssuerType = DocumentIssuerType.Own,
            InvoiceType = nfeReturn ? PurchaseInvoiceType.Return : PurchaseInvoiceType.Normal, IsNfeReturn = nfeReturn,
            InvoiceStatus = status, NfeStatus = nfeStatus, IssueDate = new DateTime(2026, 10, 5), NetWeight = 1000m,
            GrossWeight = 1000m, NfeRandomCode = nfeStatus == NfeStatus.None ? null : "12345678",
            TaxDocumentNumber = nfeStatus == NfeStatus.None ? null : "000000010", TaxDocumentSeries = "9",
            PurchaseInvoiceOriginKey = origin?.Key,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m,
            PurchaseInvoiceItemOriginKey = origin?.Items.Single().Key,
        });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, invoice);
    }

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));

    private static async Task<PurchaseInvoice> LoadAsync(UnitOfWork db, Guid key) =>
        await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == key);

    [Fact]
    public async Task Create_never_starts_emitted()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { CardCode = "F-SP", NfeStatus = NfeStatus.Authorized, NfeProtocol = "1", IsNfeReturn = true, InvoiceType = PurchaseInvoiceType.Return };
        invoice.AddItem(new PurchaseInvoiceItem { ItemCode = "TRIGO", Quantity = 1m, UnitPrice = 1m });

        await new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.InactivePurchaseApply(db), new FakeDocNumberSequenceService()).ExecuteAsync(invoice, "tester");

        Assert.Equal(NfeStatus.None, invoice.NfeStatus);
        Assert.Null(invoice.NfeProtocol);
        Assert.False(invoice.IsNfeReturn);
    }

    [Fact]
    public async Task Processing_document_cannot_be_edited()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Processing);
        var changed = await LoadAsync(db, invoice.Key);
        changed.Comments = "x";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(invoice.Key, changed, "tester"));

        Assert.Equal("A NF-e deste documento está em processamento na SEFAZ: aguarde e use Consultar situação.", e.Message);
    }

    [Fact]
    public async Task Authorized_document_keeps_the_fiscal_header()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        var changed = await LoadAsync(db, invoice.Key);
        changed.NetWeight = 999m;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(invoice.Key, changed, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }

    [Fact]
    public async Task Authorized_document_keeps_the_volume()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        var changed = await LoadAsync(db, invoice.Key);
        changed.VolumeSpecies = "BAG";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(invoice.Key, changed, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }

    [Fact]
    public async Task Pending_document_saves_the_volume()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        var changed = await LoadAsync(db, invoice.Key);
        changed.VolumeQuantity = 20;
        changed.VolumeSpecies = "SACO";
        changed.VolumeBrand = "CEAGUI";
        changed.VolumeNumbering = "1 A 20";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        var saved = await LoadAsync(db, invoice.Key);
        Assert.Equal((20, "SACO", "CEAGUI", "1 A 20"),
            (saved.VolumeQuantity, saved.VolumeSpecies, saved.VolumeBrand, saved.VolumeNumbering));
    }

    [Fact]
    public async Task Api_never_writes_the_issuance_fields_nor_the_number_after_reservation()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        var changed = await LoadAsync(db, invoice.Key);
        changed.NfeStatus = NfeStatus.Authorized;
        changed.TaxDocumentNumber = "000000999";
        changed.IsNfeReturn = true;
        changed.Comments = "ok";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        var saved = await LoadAsync(db, invoice.Key);
        Assert.Equal(NfeStatus.Rejected, saved.NfeStatus);
        Assert.Equal("000000010", saved.TaxDocumentNumber);
        Assert.False(saved.IsNfeReturn);
        Assert.Equal("ok", saved.Comments);
    }

    [Fact]
    public async Task Reserved_number_keeps_the_branch_the_issuer_and_the_type()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        db.Context.Branchs.Add(new Branch
        {
            Code = "02", BranchName = "OUTRA", ShortName = "OUTRA", TaxId = "98765432000110", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = true,
        });
        await db.SaveChangesAsync();
        var changed = await LoadAsync(db, invoice.Key);
        changed.BranchCode = "02";
        changed.IssuerType = DocumentIssuerType.ThirdParty;
        changed.InvoiceType = PurchaseInvoiceType.Return;
        changed.Comments = "ok";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        var saved = await LoadAsync(db, invoice.Key);
        Assert.Equal("01", saved.BranchCode);
        Assert.Equal(DocumentIssuerType.Own, saved.IssuerType);
        Assert.Equal(PurchaseInvoiceType.Normal, saved.InvoiceType);
        Assert.Equal("ok", saved.Comments);
    }

    [Fact]
    public async Task Patch_cannot_write_the_cancellation_fields()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        var changed = await LoadAsync(db, invoice.Key);
        changed.NfeCancellationProtocol = "999";
        changed.NfeCancelledAt = DateTime.Now;
        changed.NfeCancellationReason = "escrito pela API indevidamente";
        changed.NfeCancellationError = "x";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        var saved = await LoadAsync(db, invoice.Key);
        Assert.Null(saved.NfeCancellationProtocol);
        Assert.Null(saved.NfeCancelledAt);
        Assert.Null(saved.NfeCancellationReason);
        Assert.Null(saved.NfeCancellationError);
    }

    [Fact]
    public async Task Document_without_a_reserved_number_still_accepts_a_branch_change()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        var changed = await LoadAsync(db, invoice.Key);
        changed.BranchCode = "02";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        Assert.Equal("02", (await LoadAsync(db, invoice.Key)).BranchCode);
    }

    [Fact]
    public async Task Line_cannot_be_added_while_authorized()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(new PurchaseInvoiceItem { PurchaseInvoiceKey = invoice.Key, ItemCode = "TRIGO", Quantity = 1m }, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }

    [Fact]
    public async Task Purchase_return_does_not_take_new_lines()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(new PurchaseInvoiceItem { PurchaseInvoiceKey = invoice.Key, ItemCode = "TRIGO", Quantity = 1m }, "tester"));

        Assert.Equal("A devolução de compra só tem os itens que vieram da entrada: não é possível incluir item.", e.Message);
    }

    [Fact]
    public async Task Purchase_return_line_only_changes_the_quantity()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);
        var line = invoice.Items.Single();
        var changed = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        changed.Quantity = 400m;
        changed.UnitPrice = 9m;
        changed.ItemCode = "MILHO";

        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
            .ExecuteAsync(line.Key!.Value, changed, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        Assert.Equal(400m, saved.Quantity);
        Assert.Equal(1.5m, saved.UnitPrice);
        Assert.Equal("TRIGO", saved.ItemCode);
    }

    [Fact]
    public async Task Purchase_return_header_keeps_supplier_branch_and_type()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);
        var changed = await LoadAsync(db, invoice.Key);
        changed.CardCode = "OUTRO";
        changed.BranchCode = "02";
        changed.InvoiceType = PurchaseInvoiceType.Normal;
        changed.Comments = "motivo revisto";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        var saved = await LoadAsync(db, invoice.Key);
        Assert.Equal("F-SP", saved.CardCode);
        Assert.Equal("01", saved.BranchCode);
        Assert.Equal(PurchaseInvoiceType.Return, saved.InvoiceType);
        Assert.Equal("motivo revisto", saved.Comments);
    }

    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Denied)]
    public async Task Emitted_document_cannot_be_deleted(NfeStatus status)
    {
        var (db, invoice) = await SeedAsync(status);

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key));

        Assert.Equal("Documento com NF-e em processamento, autorizada, denegada, cancelada ou inutilizada não pode ser excluído.", e.Message);
    }

    [Fact]
    public async Task Rejected_document_deletes_together_with_its_xml_rows()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        db.Context.PurchaseInvoiceNfeXmls.Add(new PurchaseInvoiceNfeXml
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, Kind = NfeXmlKind.Signed, Xml = "<NFe/>", CreatedAt = DateTime.Now,
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        await new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key);

        Assert.False(await db.Context.PurchaseInvoices.AnyAsync(i => i.Key == invoice.Key));
        Assert.False(await db.Context.PurchaseInvoiceNfeXmls.AnyAsync(x => x.PurchaseInvoiceKey == invoice.Key));
    }

    [Fact]
    public async Task Authorized_document_cannot_be_cancelled_here()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(
            NfeLockRules.AuthorizedCancelMessage,
            e.Message);
    }

    [Fact]
    public async Task Own_document_in_a_branch_that_issues_nfe_confirms_only_by_the_emission()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            TaxTestServices.PurchaseConfirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.", e.Message);
    }

    [Fact]
    public async Task Authorized_own_document_confirms()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);

        await TaxTestServices.PurchaseConfirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Third_party_document_confirms_as_before()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        var entity = await db.Context.PurchaseInvoices.SingleAsync(i => i.Key == invoice.Key);
        entity.IssuerType = DocumentIssuerType.ThirdParty;
        entity.TaxDocumentKind = TaxDocumentKind.Other;
        await db.SaveChangesAsync();

        await TaxTestServices.PurchaseConfirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Update_cannot_turn_an_own_entry_into_a_manual_purchase_return()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        var changed = await LoadAsync(db, invoice.Key);
        changed.InvoiceType = PurchaseInvoiceType.Return;

        var update = new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, new FakeBusinessPartnerService(), "STANDALONE"));
        var e = await Assert.ThrowsAsync<DefaultException>(() => update.ExecuteAsync(invoice.Key, changed, "tester"));

        Assert.Equal(
            "Na filial que emite NF-e pelo Siagro, a devolução de compra é feita pelo botão Devolver, no detalhe do documento de entrada.",
            e.Message);
    }

    [Fact]
    public async Task Line_nature_chosen_later_reaches_the_calculation()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        db.Context.Items.Add(new Item { ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", Ncm = "10019900", GoodsOrigin = 0 });
        var first = new Usage
        {
            Name = "COMPRA A", Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
            IcmsInStateCst = "00", IcmsInStateRate = 18m, IcmsOutStateCst = "00", PisCst = "74", CofinsCst = "74",
        };
        var second = new Usage
        {
            Name = "COMPRA B", Direction = UsageDirection.Incoming, CfopIncomingInState = "1101", CfopIncomingOutState = "2101",
            IcmsInStateCst = "00", IcmsInStateRate = 18m, IcmsOutStateCst = "00", PisCst = "74", CofinsCst = "74",
        };
        db.Context.Usages.AddRange(first, second);
        await db.SaveChangesAsync();
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { ["F-SP"] = "PRODUTOR TESTE" },
            states: new Dictionary<string, string> { ["F-SP"] = "SP" });
        var service = new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(),
            TaxTestServices.PurchaseApply(db, partners, "STANDALONE"));
        var lineKey = invoice.Items.Single().Key!.Value;

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == lineKey);
        line.UsageCode = first.Code;
        await service.ExecuteAsync(lineKey, line, "tester");
        Assert.Equal("1102", (await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == lineKey)).Cfop);

        db.Context.ChangeTracker.Clear();
        line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == lineKey);
        line.UsageCode = second.Code;
        await service.ExecuteAsync(lineKey, line, "tester");

        Assert.Equal("1101", (await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == lineKey)).Cfop);
    }

    [Fact]
    public async Task Authorized_line_cannot_change_the_discount()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        var changed = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == invoice.Items.Single().Key);
        changed.DiscountValue = 10m;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(changed.Key!.Value, changed, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }

    [Fact]
    public async Task Purchase_return_line_keeps_the_edited_charges()
    {
        // Review Focus 5: na devolução Pendente os quatro valores são editáveis (spec D4); a trava volta preço e produto.
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);
        var line = invoice.Items.Single();
        var changed = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        (changed.FreightValue, changed.DiscountValue, changed.UnitPrice) = (15m, 2m, 9m);

        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
            .ExecuteAsync(line.Key!.Value, changed, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        Assert.Equal((15m, 2m, 1.5m), (saved.FreightValue, saved.DiscountValue, saved.UnitPrice));
    }

    [Fact]
    public void Cancelled_nfe_document_cannot_be_deleted()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            PurchaseInvoiceNfeLock.EnsureDeletable(new PurchaseInvoice { CardCode = "F-SP", NfeStatus = NfeStatus.Cancelled }));

        Assert.Contains("cancelada", ex.Message);
    }

    [Theory]
    [InlineData(NfeStatus.Authorized, "justificativa")]
    [InlineData(NfeStatus.Cancelled, "Concluir cancelamento")]
    public void Common_cancel_of_emitted_nfe_points_to_the_right_path(NfeStatus status, string expected)
    {
        var ex = Assert.Throws<DefaultException>(() =>
            PurchaseInvoiceNfeLock.EnsureCancellable(new PurchaseInvoice { CardCode = "F-SP", NfeStatus = status }));

        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Reverse_is_refused_for_emitted_nfe(NfeStatus status)
    {
        var (db, invoice) = await SeedAsync(status, InvoiceStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(NfeLockRules.EmittedReverseMessage, ex.Message);
    }
}
