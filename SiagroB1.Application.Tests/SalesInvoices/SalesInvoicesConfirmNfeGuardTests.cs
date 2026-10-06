using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Com a regra ativa, o documento Normal confirma pelo Confirmar e transmite a NF-e depois (spec 2026-10-06 D1); só a
/// devolução própria confirma com a NF-e autorizada. SAPB1 e STANDALONE sem a chave confirmam como sempre.
/// </summary>
public class SalesInvoicesConfirmNfeGuardTests
{
    private static SalesInvoicesConfirmService Confirm(UnitOfWork db, string erp)
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesConfirmService(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(usages),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            TaxTestServices.Gate(db, erp));
    }

    /// <summary>Documento AVULSO de uma linha, natureza sem efeito no contrato, filial com a chave.</summary>
    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(
        bool issuesNfe = true, NfeStatus nfe = NfeStatus.None, SalesInvoiceType type = SalesInvoiceType.Normal,
        bool nfeReturn = false)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "CEAGUI", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe });
        await db.SaveChangesAsync();

        var usage = await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102", RequiresQuantity = false,
        });

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);

        // Devolução: precisa de uma origem confirmada com a linha que ela devolve.
        var origin = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Confirmed);
        origin.BranchCode = "01";
        var originItem = SalesContractsAllocationTestSupport.NewItem(origin, contract.Key, releaseKey: null, 100m);
        originItem.UsageCode = usage.Code;

        var invoice = SalesContractsAllocationTestSupport.NewInvoice(
            InvoiceStatus.Pending, type, originKey: type == SalesInvoiceType.Return ? origin.Key : null);
        invoice.BranchCode = "01";
        invoice.NfeStatus = nfe;
        invoice.IsNfeReturn = nfeReturn;
        invoice.NetWeight = invoice.GrossWeight = 100m; // devolução: o peso do cabeçalho fecha com a linha
        SalesContractsAllocationTestSupport.NewItem(
            invoice, contract.Key, releaseKey: null, 100m,
            originItemKey: type == SalesInvoiceType.Return ? originItem.Key : null).UsageCode = usage.Code;

        db.Context.SalesContracts.Add(contract);
        if (type == SalesInvoiceType.Return)
            db.Context.SalesInvoices.Add(origin);
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (db, invoice);
    }

    private static async Task<InvoiceStatus?> StatusAsync(UnitOfWork db, Guid key) =>
        (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == key)).InvoiceStatus;

    [Fact]
    public async Task Rule_active_confirms_a_normal_document_directly()
    {
        var (db, invoice) = await SeedAsync();

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Rule_active_confirms_a_document_of_kind_other()
    {
        var (db, invoice) = await SeedAsync();
        invoice.TaxDocumentKind = TaxDocumentKind.Other;
        await db.SaveChangesAsync();

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    /// <summary>Devolução não-própria confirma diretamente: a guarda só vale para a devolução própria (IsNfeReturn=true).</summary>
    [Fact]
    public async Task Rule_active_still_confirms_a_return_directly()
    {
        var (db, invoice) = await SeedAsync(type: SalesInvoiceType.Return);

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Rule_active_confirms_with_authorized_nfe()
    {
        var (db, invoice) = await SeedAsync(nfe: NfeStatus.Authorized);

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Sapb1_with_the_flag_in_the_database_confirms_as_today()
    {
        var (db, invoice) = await SeedAsync();

        await Confirm(db, "SAPB1").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Standalone_without_the_flag_confirms_as_today()
    {
        var (db, invoice) = await SeedAsync(issuesNfe: false);

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Rule_active_refuses_direct_confirmation_of_an_own_return()
    {
        var (db, invoice) = await SeedAsync(type: SalesInvoiceType.Return, nfeReturn: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.", ex.Message);
    }

    [Fact]
    public async Task Own_return_with_authorized_nfe_is_confirmed()
    {
        var (db, invoice) = await SeedAsync(type: SalesInvoiceType.Return, nfeReturn: true, nfe: NfeStatus.Authorized);

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }
}
