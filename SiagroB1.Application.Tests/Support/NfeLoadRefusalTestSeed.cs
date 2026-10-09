using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <param name="DatabaseName">A base InMemory do cenário — para abrir um segundo contexto com interceptor de falha.</param>
public sealed record NfeLoadRefusalScenario(UnitOfWork Db, ShipmentLoad Load, IReadOnlyList<Guid> SaleKeys, string DatabaseName);

/// <summary>
/// Carga faturada na CEAGUI (filial com NF-e pelo Siagro): a venda AUTORIZADA do <see cref="NfeReturnTestSeed"/>
/// — e, com <c>documents = 2</c>, uma segunda venda autorizada igual — pendurada numa carga de 30 t por venda,
/// com o romaneio de embarque. A carga sai do recálculo REAL (Faturada).
/// </summary>
public static class NfeLoadRefusalTestSeed
{
    public const string OriginWarehouse = "ARM01";
    public const string DestinationWarehouse = "ARM99";

    public static async Task<NfeLoadRefusalScenario> SeedAsync(int documents = 1)
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var db = s.Sale.Db;
        var context = db.Context;

        var sale = await context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        var keys = new List<Guid> { sale.Key };

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG", TruckCode = "ABC-1D23", CarrierCardCode = "T-001",
            CarrierName = "TRANSPORTADORA TESTE LTDA", WarehouseCode = OriginWarehouse,
            TotalQuantity = 30_000m * documents, Status = ShipmentLoadStatus.Open,
        };
        context.ShipmentLoads.Add(load);
        context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = NfeTestSeed.CardCode, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse, BranchCode = "01", TruckCode = "ABC-1D23",
            GrossWeight = 30_000m * documents, NetWeight = 30_000m * documents,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed, ShipmentLoadKey = load.Key,
        });
        sale.ShipmentLoadKey = load.Key;

        if (documents == 2)
        {
            var item = sale.Items.Single();
            var second = new SalesInvoice
            {
                Key = Guid.NewGuid(), BranchCode = "01", CardCode = NfeTestSeed.CardCode, CardName = sale.CardName,
                InvoiceType = SalesInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed, InvoiceNumber = "000002389",
                InvoiceDate = sale.InvoiceDate, GrossWeight = 30_000m, NetWeight = 30_000m,
                TruckingCompanyCode = "T-001", TruckCode = "ABC-1D23", FreightTerms = FreightTerms.Cif,
                ShipmentLoadKey = load.Key, NfeStatus = NfeStatus.Authorized, TaxDocumentNumber = "000000002",
                TaxDocumentSeries = "1", NfeRandomCode = "48151624",
                ChaveNFe = "35261012345678000195550010000000021481516240",
            };
            second.AddItem(new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
                Quantity = 30_000m, UnitPrice = 2m, UsageCode = item.UsageCode, UsageName = item.UsageName,
                Cfop = item.Cfop, Ncm = item.Ncm, GoodsOrigin = 0, CstIcms = item.CstIcms, IcmsRate = item.IcmsRate,
                CstPis = item.CstPis, CstCofins = item.CstCofins, IbsCbsCst = item.IbsCbsCst,
                IbsCbsClassCode = item.IbsCbsClassCode, CbsRate = item.CbsRate, IbsStateRate = item.IbsStateRate,
                NfeItemNumber = 1,
            });
            context.SalesInvoices.Add(second);
            keys.Add(second.Key);
        }

        await db.SaveChangesAsync();
        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(context, load.Key, excludedInvoiceKeys: null);
        await db.SaveChangesAsync();

        return new NfeLoadRefusalScenario(db, load, keys, s.Sale.DatabaseName);
    }

    private static FakeBusinessPartnerService Partners() =>
        new(names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA", ["T-001"] = "TRANSPORTADORA TESTE LTDA" },
            states: new() { [NfeTestSeed.CardCode] = "BA" });

    private static FakeItemService Items() => new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    public static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string> { [OriginWarehouse] = "ARMAZEM ORIGEM", [DestinationWarehouse] = "ARMAZEM RETAGUARDA" });

    private static SalesInvoicesCreateService Create(UnitOfWork db)
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var partners = Partners();

        return new SalesInvoicesCreateService(
            db, partners, Items(), new FakeDocNumberSequenceService(), new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners), TaxTestServices.Apply(db, partners, "STANDALONE"),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);
    }

    public static ShipmentLoadRefusalEffectsService Effects(IUnitOfWork db) =>
        new(db,
            new StorageTransactionsCreateService(
                db, new FakeDocNumberSequenceService(), Partners(), Items(), Warehouses(),
                new ShipmentReleasesRecalculateShippedService(db.Context), new ShipmentReleaseMovementGuardService(db.Context),
                NullLogger<StorageTransactionsCreateService>.Instance),
            new StorageTransactionsConfirmedService(
                db, new FakeStringLocalizer<Resource>(), new ShipmentReleasesRecalculateShippedService(db.Context),
                new ShipmentReleaseMovementGuardService(db.Context), NullLogger<StorageTransactionsConfirmedService>.Instance),
            new ShipmentLoadsMovementLogService(db.Context),
            new ShipmentReleasesFromReturnService(db.Context));

    /// <summary>Segundo tempo da recusa (spec 2026-10-09 §5.3), com os efeitos reais do destino.</summary>
    public static ShipmentLoadRefusalCompleteService Complete(IUnitOfWork db) => new(db, Effects(db));

    /// <param name="complete">
    /// Conclusão da recusa. <c>null</c> (padrão) é a confirmação do fluxo síncrono, a que <see cref="RefuseService"/> usa.
    /// </param>
    public static SalesInvoicesConfirmService ConfirmService(IUnitOfWork db, ShipmentLoadRefusalCompleteService? complete = null) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(new UsageService(db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            TaxTestServices.Gate(db, "STANDALONE"),
            complete);

    public static ShipmentLoadsRefuseService RefuseService(UnitOfWork db, string erp = "STANDALONE") =>
        new(db,
            Create(db),
            ConfirmService(db),
            Effects(db),
            new ShipmentLoadsMovementLogService(db.Context),
            Warehouses(),
            NullLogger<ShipmentLoadsRefuseService>.Instance,
            TaxTestServices.Gate(db, erp),
            new SalesInvoiceNfeReturnBuilder(db, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone));

    /// <summary>Cancelamento do documento de saída com as dependências reais do banco.</summary>
    public static SalesInvoicesCancelService CancelService(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

    /// <summary>"Cancelar recusa" (spec 2026-10-09 §5.4).</summary>
    public static ShipmentLoadsCancelRefusalService CancelRefusalService(UnitOfWork db) =>
        new(db, CancelService(db), new ShipmentLoadsMovementLogService(db.Context),
            NullLogger<ShipmentLoadsCancelRefusalService>.Instance);

    /// <summary>O que o retorno da SEFAZ faz: grava Autorizada e confirma em transação própria (Auto).</summary>
    /// <param name="confirm">Padrão: <see cref="ConfirmService"/> do mesmo banco.</param>
    public static async Task AuthorizeAndConfirmAsync(UnitOfWork db, Guid returnKey, SalesInvoicesConfirmService? confirm = null)
    {
        var invoice = await db.Context.SalesInvoices.SingleAsync(i => i.Key == returnKey);
        invoice.NfeStatus = NfeStatus.Authorized;
        await db.SaveChangesAsync();

        await (confirm ?? ConfirmService(db)).ExecuteAsync(returnKey, "tester");
    }
}
