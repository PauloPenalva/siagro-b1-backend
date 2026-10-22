using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Commons.Resources;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Cenário dos firmes a receber (spec 2026-10-15): contrato de venda VC-1 (C0001, aprovado), provisório a receber
/// FN-PROV-V1, conta CX01 e duas naturezas de saída que consomem saldo (uma gera financeiro, outra não). Documentos de
/// saída AVULSOS de uma linha: 1.000 × R$ 90,00 = 90.000,00.
/// </summary>
internal sealed class SalesFirmReceivableSeed
{
    public const decimal DefaultLineAmount = 90_000m;

    public UnitOfWork Db { get; }
    public AppDbContext Context => Db.Context;
    public FakeDocNumberSequenceService Numbers { get; } = new();
    public string Erp { get; private init; } = "STANDALONE";
    public SalesContract Contract { get; private set; } = null!;
    public FinancialDocument Provisional { get; private set; } = null!;
    public int FinancialUsageCode { get; private set; }
    public int NonFinancialUsageCode { get; private set; }

    private SalesFirmReceivableSeed(UnitOfWork db) => Db = db;

    public static async Task<SalesFirmReceivableSeed> CreateAsync(decimal provisionalAmount = 100_000m, string erp = "STANDALONE")
    {
        var seed = new SalesFirmReceivableSeed(TestDb.CreateUnitOfWork()) { Erp = erp };
        seed.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        var usages = new UsageService(seed.Db, NullLogger<UsageService>.Instance);
        seed.FinancialUsageCode = (await usages.CreateAsync(Usage("Venda de produção", createsFinancial: true))).Code;
        seed.NonFinancialUsageCode = (await usages.CreateAsync(Usage("Saída sem financeiro", createsFinancial: false))).Code;
        seed.Contract = await seed.AddContractAsync("VC-1");
        seed.Provisional = await seed.AddProvisionalAsync(seed.Contract, provisionalAmount, "FN-PROV-V1");
        return seed;
    }

    private static UsageModel Usage(string name, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
        ContractBalanceEffect = ContractBalanceEffect.Consume, RequiresContract = true, RequiresQuantity = true,
        CreatesFinancialDocument = createsFinancial,
    };

    public async Task<SalesContract> AddContractAsync(string code)
    {
        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m);
        contract.Code = code;
        Context.SalesContracts.Add(contract);
        await Context.SaveChangesAsync();
        return contract;
    }

    public async Task<FinancialDocument> AddProvisionalAsync(SalesContract contract, decimal amount, string code,
        DateTime? createdAt = null)
    {
        var provisional = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "C0001", BranchCode = "01",
            Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Provisional,
            Status = FinancialDocumentStatus.Open, DueDate = new DateTime(2026, 12, 31), NetAmount = amount,
            OriginType = FinancialDocumentOrigin.SalesContractPriceFixation, OriginKey = Guid.NewGuid(),
            OriginDocNumber = contract.Code, SalesContractKey = contract.Key, Currency = CurrencyType.Brl,
            CreatedAt = createdAt ?? DateTime.Now,
        };
        Context.FinancialDocuments.Add(provisional);
        await Context.SaveChangesAsync();
        return provisional;
    }

    /// <summary>Documento de saída AVULSO de uma linha, gravado. Por padrão Pendente, com a natureza que gera financeiro.</summary>
    public async Task<(SalesInvoice Invoice, SalesInvoiceItem Line)> AddInvoiceAsync(
        decimal quantity = 1_000m, decimal unitPrice = 90m, SalesContract? contract = null, bool withContract = true,
        int? usageCode = null, bool withUsage = true, InvoiceStatus status = InvoiceStatus.Pending,
        SalesInvoiceType type = SalesInvoiceType.Normal, Guid? originInvoiceKey = null, Guid? originItemKey = null,
        string number = "DS000001")
    {
        var target = contract ?? Contract;
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(status, type, originKey: originInvoiceKey);
        invoice.BranchCode = "01";
        invoice.InvoiceNumber = number;
        invoice.CardName = "CLIENTE TESTE";
        invoice.TaxDocumentNumber = "7001";
        invoice.TaxDocumentSeries = "1";
        invoice.InvoiceDate = new DateTime(2026, 10, 15);
        var line = SalesContractsAllocationTestSupport.NewItem(
            invoice, withContract ? target.Key : null, releaseKey: null, quantity, unitPrice, originItemKey);
        line.UsageCode = withUsage ? usageCode ?? FinancialUsageCode : null;
        if (type == SalesInvoiceType.Return)
            invoice.NetWeight = quantity;   // a confirmação da devolução confere peso × itens
        Context.SalesInvoices.Add(invoice);
        await Context.SaveChangesAsync();
        return (invoice, line);
    }

    /// <summary>Linha de ledger gravada (sem passar pelos serviços de alocação).</summary>
    public async Task AllocateAsync(SalesInvoiceItem line, SalesContract contract, decimal volume,
        SalesContractAllocationOrigin origin = SalesContractAllocationOrigin.Billing)
    {
        Context.SalesContractsAllocations.Add(
            SalesContractsAllocationTestSupport.NewAllocation(contract.Key, line.Key!.Value, volume, origin: origin));
        await Context.SaveChangesAsync();
    }

    public SalesInvoicesConfirmService Confirm(FinancialDocumentsSalesNoContractService? salesNoContract = null,
        SiagroB1.Application.Services.Accounting.Postings.IAccountingPostingService? accounting = null) => new(Db,
        new SalesShipmentReleasesRecalculateShippedService(Context),
        new SalesContractsAllocationCreateService(Db, new SalesContractsFixedVolumeService(Context)),
        new SalesContractsAllocationCreateForReturnService(Db, new SalesContractsFixedVolumeService(Context)),
        new SalesInvoicesUsageGuardService(new UsageService(Db, NullLogger<UsageService>.Instance)),
        new SalesContractsAllocationCreateForFiscalAdjustmentService(Db, new SalesContractsFixedVolumeService(Context)),
        new ShipmentLoadsBalanceHookService(Context, new ShipmentLoadsMovementLogService(Context)),
        new ShipmentLoadsClosureHookService(Context, new ShipmentLoadsChangeLogService(Context)),
        new FakeStringLocalizer<Resource>(),
        salesFirmSync: Sync(),
        salesNoContract: salesNoContract,
        accounting: accounting);

    public SalesInvoicesReverseConfirmService ReverseConfirm(FinancialDocumentsSalesNoContractService? salesNoContract = null,
        SiagroB1.Application.Services.Accounting.Postings.IAccountingPostingService? accounting = null) => new(Db,
        new SalesContractsAllocationDeleteForInvoiceService(Db),
        new ShipmentReleasesRecalculateShippedService(Context),
        new ShipmentLoadsBalanceHookService(Context, new ShipmentLoadsMovementLogService(Context)),
        new ShipmentLoadsClosureHookService(Context, new ShipmentLoadsChangeLogService(Context)),
        new FakeStringLocalizer<Resource>(),
        salesFirmSync: Sync(),
        salesNoContract: salesNoContract,
        accounting: accounting);

    public SalesInvoicesCancelService Cancel(FinancialDocumentsSalesNoContractService? salesNoContract = null,
        SiagroB1.Application.Services.Accounting.Postings.IAccountingPostingService? accounting = null) => new(Db,
        new SalesShipmentReleasesRecalculateShippedService(Context),
        new SalesContractsAllocationDeleteForInvoiceService(Db),
        new ShipmentLoadsBalanceHookService(Context, new ShipmentLoadsMovementLogService(Context)),
        NullLogger<SalesInvoicesCancelService>.Instance,
        salesFirmSync: Sync(),
        salesNoContract: salesNoContract,
        accounting: accounting);

    /// <summary>Documento avulso Pendente confirmado pelo serviço REAL (ledger FiscalAdjustment + firme).</summary>
    public async Task<(SalesInvoice Invoice, SalesInvoiceItem Line)> ConfirmedAsync(
        decimal quantity = 1_000m, decimal unitPrice = 90m, string number = "DS000001")
    {
        var (invoice, line) = await AddInvoiceAsync(quantity, unitPrice, number: number);
        await Confirm().ExecuteAsync(invoice.Key, "ana");
        return (invoice, line);
    }

    public FinancialDocumentsSalesFirmSyncService Sync() => FinancialDocumentTestServices.SalesFirmSync(Db, Numbers, Erp);

    public async Task SyncAsync(SalesInvoiceItem line, SalesFirmSyncTrigger trigger = SalesFirmSyncTrigger.Document)
    {
        await Sync().EnqueueSyncAsync([line.Key!.Value], "ana", trigger);
        await Db.SaveChangesAsync();
    }

    public Task<List<FinancialDocument>> TitlesOfAsync(SalesInvoiceItem line, bool includeCanceled = false) =>
        Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice && d.OriginKey == line.Key &&
                        (includeCanceled || d.Status != FinancialDocumentStatus.Canceled))
            .OrderBy(d => d.Code)
            .ToListAsync();

    public Task ComposeAsync(FinancialDocument document, FinancialDocument? provisional = null) =>
        new FinancialDocumentsComposeService(Db, new FinancialDocumentChangeLogService(Context))
            .ExecuteAsync((provisional ?? Provisional).Key, [document.Key], "ana");

    public async Task<FinancialDocument> ReloadAsync(Guid key) =>
        await Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);

    /// <summary>Muda o status do documento direto no banco (para exercitar a sincronização sem os serviços).</summary>
    public async Task SetStatusAsync(SalesInvoice invoice, InvoiceStatus status)
    {
        var tracked = await Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
        tracked.InvoiceStatus = status;
        await Context.SaveChangesAsync();
    }
}
