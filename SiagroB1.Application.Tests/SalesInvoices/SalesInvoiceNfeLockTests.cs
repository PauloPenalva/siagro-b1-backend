using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Travas do documento com NF-e (spec §9.4 + decisão P11 do plano). Em processamento nada muda; com
/// a NF-e autorizada, o que foi ao XML não muda (inclusive depois de estornar a confirmação), e o
/// cancelamento espera o cancelamento na SEFAZ (2b). Os campos da NF-e só a emissão escreve.
/// </summary>
public class SalesInvoiceNfeLockTests
{
    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(
        NfeStatus nfe, InvoiceStatus status = InvoiceStatus.Pending)
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceStatus = status, NfeStatus = nfe,
            InvoiceDate = new DateTime(2026, 10, 2), Comments = "antes",
            Items =
            [
                new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m },
            ],
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(names: new() { ["C1"] = "CLIENTE", ["C2"] = "OUTRO" }),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    [Fact]
    public async Task Processing_document_header_cannot_be_edited()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Processing);
        invoice.Comments = "depois";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("em processamento", ex.Message);
    }

    [Fact]
    public async Task Authorized_document_cannot_change_the_customer()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.CardCode = "C2";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("autorizada", ex.Message);
    }

    [Fact]
    public async Task Authorized_document_cannot_change_the_volume()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.VolumeSpecies = "SACO";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("autorizada", ex.Message);
    }

    [Fact]
    public async Task Authorized_document_can_change_internal_comments()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.Comments = "depois";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("depois", (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).Comments);
    }

    [Fact]
    public async Task Patch_cannot_write_the_issuance_fields()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeProtocol = "123";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.None, saved.NfeStatus);
        Assert.Null(saved.NfeProtocol);
    }

    /// <summary>Pelo serviço de criação: um corpo "Autorizada" passaria pela guarda da confirmação direta.</summary>
    [Fact]
    public async Task Create_ignores_issuance_fields_in_the_body()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "SP" });
        await db.SaveChangesAsync();

        var partners = new FakeBusinessPartnerService(names: new() { ["C1"] = "CLIENTE" }, states: new() { ["C1"] = "SP" });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        await usages.CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            RequiresQuantity = true, IsDefault = true,
        });

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", InvoiceDate = new DateTime(2026, 10, 2),
            NfeStatus = NfeStatus.Authorized, NfeProtocol = "123456", NfeRandomCode = "12345678",
            NfeConfirmationError = "x",
            Items =
            [
                new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
                    SalesContractKey = Guid.NewGuid(),
                },
            ],
        };

        await new SalesInvoicesCreateService(db, partners,
                new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
                new FakeDocNumberSequenceService(), new SalesInvoicesUsageGuardService(usages),
                new SalesInvoicesCfopResolveService(db, usages, partners), TaxTestServices.InactiveApply(db),
                NullLogger<SalesInvoicesCreateService>.Instance)
            .ExecuteAsync(invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == invoice.Key);
        Assert.Equal(NfeStatus.None, saved.NfeStatus);
        Assert.Null(saved.NfeProtocol);
        Assert.Null(saved.NfeRandomCode);
        Assert.Null(saved.NfeConfirmationError);
    }

    /// <summary>Caminho vivo da Yokotobi e da MH Agro: o número/série/chave são digitados no formulário.</summary>
    [Fact]
    public async Task Document_without_nfe_keeps_the_tax_document_fields_writable()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        invoice.TaxDocumentNumber = "000000010";
        invoice.TaxDocumentSeries = "1";
        invoice.ChaveNFe = "35261012345678000195550010000000101000000011";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("000000010", saved.TaxDocumentNumber);
        Assert.Equal("1", saved.TaxDocumentSeries);
        Assert.Equal("35261012345678000195550010000000101000000011", saved.ChaveNFe);
    }

    [Fact]
    public async Task Emitted_document_keeps_the_tax_document_fields_unchanged()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.TaxDocumentNumber = "000000010";
        invoice.TaxDocumentSeries = "1";
        invoice.ChaveNFe = "CHAVE-ORIGINAL";
        await db.SaveChangesAsync();

        invoice.TaxDocumentNumber = "999";
        invoice.TaxDocumentSeries = "9";
        invoice.ChaveNFe = "CHAVE-FORJADA";
        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("000000010", saved.TaxDocumentNumber);
        Assert.Equal("1", saved.TaxDocumentSeries);
        Assert.Equal("CHAVE-ORIGINAL", saved.ChaveNFe);
    }

    [Fact]
    public async Task Authorized_line_cannot_change_quantity()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.Quantity = 20m;

        await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester"));
    }

    [Fact]
    public async Task Authorized_line_accepts_the_delivery_reconciliation()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.DeliveredQuantity = 9m;

        await ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester");

        Assert.Equal(9m, (await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync()).DeliveredQuantity);
    }

    [Fact]
    public async Task Line_cannot_be_added_to_an_authorized_document()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
                    TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesItemsCreateService>.Instance)
                .ExecuteAsync(new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
                }, "tester"));
    }

    [Fact]
    public async Task Line_cannot_be_removed_while_processing()
    {
        var (db, _) = await SeedAsync(NfeStatus.Processing);
        var item = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsDeleteService(db, NullLogger<SalesInvoicesItemsDeleteService>.Instance).ExecuteAsync(item.Key!.Value));
    }

    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Denied)]
    public async Task Document_with_nfe_cannot_be_deleted(NfeStatus nfe)
    {
        var (db, invoice) = await SeedAsync(nfe);

        await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Rejected_document_can_be_deleted()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = NfeXmlKind.Signed,
            Xml = "<NFe/>", CreatedAt = DateTime.Now,
        });
        await db.SaveChangesAsync();

        var deleted = await new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(invoice.Key, "tester");

        Assert.True(deleted);
        Assert.False(await db.Context.SalesInvoiceNfeXmls.AnyAsync(x => x.SalesInvoiceKey == invoice.Key));
    }

    /// <summary>A remoção dos XMLs vem depois de todas as guardas: recusado o delete, nada fica marcado.</summary>
    [Fact]
    public async Task Refused_delete_does_not_leave_the_xmls_marked_for_removal()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = NfeXmlKind.Signed,
            Xml = "<NFe/>", CreatedAt = DateTime.Now,
        });
        db.Context.ShipmentLoadsDischargesItems.Add(new ShipmentLoadDischargeItem
        {
            Key = Guid.NewGuid(), DischargeKey = Guid.NewGuid(), SalesInvoiceKey = invoice.Key,
            SalesInvoiceItemKey = invoice.Items.Single().Key!.Value, Quantity = 1m,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(invoice.Key, "tester"));

        Assert.DoesNotContain(db.Context.ChangeTracker.Entries<SalesInvoiceNfeXml>(), e => e.State == EntityState.Deleted);
    }

    [Fact]
    public async Task Document_with_authorized_nfe_cannot_be_cancelled()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesCancelService(db,
                new SalesShipmentReleasesRecalculateShippedService(db.Context),
                new SalesContractsAllocationDeleteForInvoiceService(db),
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesCancelService>.Instance)
            .ExecuteAsync(invoice.Key, "tester"));

        Assert.Contains("SEFAZ", ex.Message);
    }

    [Fact]
    public async Task Manual_tax_document_is_refused_after_issuance()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesSetDocumentNumberService(db, new SalesInvoicesChangeLogService(db.Context))
                .ExecuteAsync(invoice.Key, "000000010", "1", null, "tester"));
    }

    /// <summary>Número reservado (cNF gravado) já é da emissão, mesmo com a situação ainda None.</summary>
    [Fact]
    public async Task Reserved_number_cannot_be_overwritten_by_a_patch()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        invoice.NfeRandomCode = "12345678";
        invoice.TaxDocumentNumber = "000000007";
        invoice.TaxDocumentSeries = "1";
        await db.SaveChangesAsync();

        invoice.TaxDocumentNumber = "999";
        invoice.TaxDocumentSeries = "9";
        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("000000007", saved.TaxDocumentNumber);
        Assert.Equal("1", saved.TaxDocumentSeries);
    }

    [Fact]
    public async Task Reserved_number_refuses_informar_nota_fiscal()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        invoice.NfeRandomCode = "12345678";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesSetDocumentNumberService(db, new SalesInvoicesChangeLogService(db.Context))
                .ExecuteAsync(invoice.Key, "000000010", "1", null, "tester"));
    }

    [Fact]
    public async Task Authorized_line_cannot_change_the_freight()
    {
        var (db, _) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.FreightValue = 10m;

        var e = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }
}
