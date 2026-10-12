using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Contrato de compra + liberação no armazém de terceiro ARM001, e as naturezas de remessa e de retorno.</summary>
public sealed class StorageDepositSeed
{
    public const string Warehouse = "ARM001";
    public const string Supplier = "F0001";
    public const string Item = "SOJA";
    public const string Branch = "01";

    private readonly UnitOfWork _db;
    private StorageDepositSeed(UnitOfWork db) => _db = db;

    public required PurchaseContract Contract { get; init; }
    public required ShipmentRelease Release { get; init; }
    public int RemittanceUsageCode { get; } = 905;
    public int ReturnUsageCode { get; } = 906;

    public static async Task<StorageDepositSeed> CreateAsync(UnitOfWork db, decimal released = 100_000m)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "00000001", BranchCode = Branch, CardCode = Supplier, ItemCode = Item,
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2526", DeliveryLocationCode = Warehouse,
            Status = ContractStatus.Approved,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), BranchCode = Branch, PurchaseContractKey = contract.Key, ReleasedQuantity = released,
            DeliveryLocationCode = Warehouse, DeliveryLocationName = "ARMAZEM TERCEIRO", Status = ReleaseStatus.Actived,
        };

        db.Context.Branchs.Add(new Branch { Code = Branch, BranchName = "Matriz", ShortName = "MTZ", StateCode = "SP" });
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(release);
        db.Context.Usages.AddRange(
            new Usage { Code = 905, Name = "REMESSA P/ DEPOSITO", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5905", CfopOutgoingOutState = "6905" },
            new Usage { Code = 906, Name = "RETORNO DE DEPOSITO", Direction = UsageDirection.Incoming, CfopIncomingInState = "1906", CfopIncomingOutState = "2906" });
        db.Context.UsageEffects.AddRange(
            new UsageEffect { UsageCode = 905, StorageDepositRole = StorageDepositRole.Remittance },
            new UsageEffect { UsageCode = 906, StorageDepositRole = StorageDepositRole.Return });
        await db.SaveChangesAsync();

        return new StorageDepositSeed(db) { Contract = contract, Release = release };
    }

    /// <summary>Outra liberação no mesmo contrato e armazém (uma remessa ativa por liberação).</summary>
    public async Task<ShipmentRelease> AddReleaseAsync(ReleaseOrigin origin = ReleaseOrigin.Standard, decimal released = 100_000m)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), BranchCode = Branch, PurchaseContractKey = Contract.Key, ReleasedQuantity = released,
            DeliveryLocationCode = Warehouse, DeliveryLocationName = "ARMAZEM TERCEIRO", Status = ReleaseStatus.Actived,
            Origin = origin,
        };
        _db.Context.ShipmentReleases.Add(release);
        await _db.SaveChangesAsync();
        return release;
    }

    public async Task<SalesInvoice> AddRemittanceAsync(
        decimal quantity, InvoiceStatus status = InvoiceStatus.Confirmed, ShipmentRelease? release = null,
        DateTime? date = null, string number = "000000123")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Warehouse, InvoiceNumber = "S" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = number, InvoiceStatus = status, ShipmentReleaseKey = (release ?? Release).Key,
            InvoiceDate = date ?? DateTime.Now.Date,
            Items = [new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m, UsageCode = RemittanceUsageCode }],
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<PurchaseInvoice> AddReturnAsync(
        SalesInvoice remittance, decimal quantity, InvoiceStatus status = InvoiceStatus.Pending,
        DateTime? date = null, string number = "000000456")
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Warehouse, InvoiceNumber = "E" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = number, InvoiceStatus = status, IssueDate = date ?? DateTime.Now.Date,
            Items = [new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = ReturnUsageCode, SalesInvoiceItemKey = remittance.Items.First().Key,
            }],
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<StorageTransaction> AddShipmentAsync(
        decimal netWeight, StorageTransactionType type = StorageTransactionType.Purchase, TransactionCode? origin = null,
        ShipmentRelease? release = null, DateTime? date = null, string? code = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code ?? "R" + Guid.NewGuid().ToString("N")[..6], BranchCode = Branch,
            CardCode = Supplier, ItemCode = Item, UnitOfMeasureCode = "KG", WarehouseCode = Warehouse,
            TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = netWeight, GrossWeight = netWeight, ShipmentReleaseKey = (release ?? Release).Key, TransactionOrigin = origin,
            TruckCode = "ABC1D23", TransactionDate = date ?? DateTime.Now.Date,
        };
        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }
}
