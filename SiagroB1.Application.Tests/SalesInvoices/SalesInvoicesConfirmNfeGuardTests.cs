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
/// Com a regra ativa, o documento Normal só confirma com a NF-e autorizada (é a emissão que chama
/// a confirmação). SAPB1 e STANDALONE sem a chave confirmam como sempre.
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
    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(bool issuesNfe = true, NfeStatus nfe = NfeStatus.None)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "CEAGUI", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe });
        await db.SaveChangesAsync();

        var usage = await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102", RequiresQuantity = false,
        });

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Pending);
        invoice.BranchCode = "01";
        invoice.NfeStatus = nfe;
        SalesContractsAllocationTestSupport.NewItem(invoice, contract.Key, releaseKey: null, 100m).UsageCode = usage.Code;

        db.Context.SalesContracts.Add(contract);
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (db, invoice);
    }

    private static async Task<InvoiceStatus?> StatusAsync(UnitOfWork db, Guid key) =>
        (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == key)).InvoiceStatus;

    [Fact]
    public async Task Rule_active_refuses_direct_confirmation()
    {
        var (db, invoice) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.", ex.Message);
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
}
