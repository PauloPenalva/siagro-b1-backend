using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Lote de armazenagem de TERCEIRO (somos o depositário) do produtor P0001, as cinco naturezas de custódia e atalhos para
/// romaneios e notas (spec 2026-10-12). Pesos bruto e líquido DIFERENTES por padrão: os seeds antigos gravam iguais e
/// escondem a troca entre eles.
/// </summary>
public sealed class StorageCustodySeed
{
    public const string Depositor = "P0001";
    public const string Item = "SOJA";
    public const string Branch = "01";
    public const string Warehouse = "ARM01";
    public const int ProcessingEntryUsage = 1901;
    public const int SymbolicReturnUsage = 5902;
    public const int StorageEntryUsage = 1905;
    public const int StorageReturnUsage = 5906;
    public const int SymbolicStorageReturnUsage = 5907;
    public const string AccessKey = "35261068583898000101550090000000011990112066";

    private readonly UnitOfWork _db;
    private StorageCustodySeed(UnitOfWork db) => _db = db;

    public required StorageAddress Lot { get; init; }

    public static async Task<StorageCustodySeed> CreateAsync(UnitOfWork db, bool requiresProcessing = true, string lotCode = "L0001")
    {
        if (!db.Context.Branchs.Any(b => b.Code == Branch))
            db.Context.Branchs.Add(new Branch { Code = Branch, BranchName = "Matriz", ShortName = "MTZ", StateCode = "SP" });

        if (!db.Context.Usages.Any(u => u.Code == ProcessingEntryUsage))
        {
            db.Context.Usages.AddRange(
                new Usage { Code = ProcessingEntryUsage, Name = "ENTRADA P/ BENEFICIAMENTO", Direction = UsageDirection.Incoming, CfopIncomingInState = "1901", CfopIncomingOutState = "2901" },
                new Usage { Code = SymbolicReturnUsage, Name = "DEVOLUCAO SIMBOLICA BENEFICIAMENTO", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5902", CfopOutgoingOutState = "6902" },
                new Usage { Code = StorageEntryUsage, Name = "ENTRADA P/ ARMAZENAGEM", Direction = UsageDirection.Incoming, CfopIncomingInState = "1905", CfopIncomingOutState = "2905" },
                new Usage { Code = StorageReturnUsage, Name = "RETORNO DE ARMAZENAGEM", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5906", CfopOutgoingOutState = "6906" },
                new Usage { Code = SymbolicStorageReturnUsage, Name = "RETORNO SIMBOLICO DE ARMAZENAGEM", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5907", CfopOutgoingOutState = "6907" });
            db.Context.UsageEffects.AddRange(
                new UsageEffect { UsageCode = ProcessingEntryUsage, StorageCustodyRole = StorageCustodyRole.ProcessingEntry },
                new UsageEffect { UsageCode = SymbolicReturnUsage, StorageCustodyRole = StorageCustodyRole.ProcessingSymbolicReturn },
                new UsageEffect { UsageCode = StorageEntryUsage, StorageCustodyRole = StorageCustodyRole.StorageEntry },
                new UsageEffect { UsageCode = StorageReturnUsage, StorageCustodyRole = StorageCustodyRole.StorageReturn },
                new UsageEffect { UsageCode = SymbolicStorageReturnUsage, StorageCustodyRole = StorageCustodyRole.StorageSymbolicReturn });
        }

        var lot = new StorageAddress
        {
            Code = lotCode, BranchCode = Branch, Description = "Lote do produtor", CardCode = Depositor, CardName = "PRODUTOR UM",
            ItemCode = Item, ItemName = "SOJA EM GRAOS", WarehouseCode = Warehouse, WarehouseName = "ARMAZEM PROPRIO", UoM = "KG",
            OwnershipType = StorageOwnershipType.ThirdParty, Nature = StorageAddressNature.Regular,
            RequiresProcessing = requiresProcessing, TransactionOrigin = TransactionCode.StorageAddress,
        };
        db.Context.StorageAddresses.Add(lot);
        await db.SaveChangesAsync();

        return new StorageCustodySeed(db) { Lot = lot };
    }

    public static int UsageOf(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => ProcessingEntryUsage,
        StorageCustodyRole.ProcessingSymbolicReturn => SymbolicReturnUsage,
        StorageCustodyRole.StorageEntry => StorageEntryUsage,
        StorageCustodyRole.StorageReturn => StorageReturnUsage,
        _ => SymbolicStorageReturnUsage,
    };

    public async Task<StorageTransaction> AddTransactionAsync(
        StorageTransactionType type = StorageTransactionType.Receipt, decimal gross = 30_000m, decimal net = 28_000m,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed, Guid? ownershipTransferKey = null,
        DateTime? date = null, string? code = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code ?? "RM" + Guid.NewGuid().ToString("N")[..6], BranchCode = Branch,
            StorageAddressCode = Lot.Code, CardCode = Depositor, CardName = "PRODUTOR UM", ItemCode = Item,
            ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG", WarehouseCode = Warehouse, TransactionType = type,
            TransactionStatus = status, GrossWeight = gross, NetWeight = net, TruckCode = "ABC1D23",
            TransactionDate = date ?? DateTime.Now.Date, OwnershipTransferKey = ownershipTransferKey,
            IsOwnershipTransfer = ownershipTransferKey is null ? null : "Y",
        };
        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }

    public async Task<OwnershipTransfer> AddTransferAsync(OwnershipTransferStatus status, string destinationLot)
    {
        var transfer = new OwnershipTransfer
        {
            Key = Guid.NewGuid(), TransferCode = "OT" + Guid.NewGuid().ToString("N")[..6], Date = DateTime.Now.Date,
            TransferStatus = status, ItemCode = Item, ItemName = "SOJA EM GRAOS", UomCode = "KG", Quantity = 5_000m,
            StorageAddressOriginCode = Lot.Code!, StorageAddressDestinationCode = destinationLot,
        };
        _db.Context.OwnershipTransfers.Add(transfer);
        await _db.SaveChangesAsync();
        return transfer;
    }

    /// <summary>Nota de custódia de ENTRADA já gravada (sem passar pela guarda).</summary>
    public async Task<PurchaseInvoice> AddPurchaseDocumentAsync(
        StorageTransaction transaction, StorageCustodyRole role, decimal quantity,
        DocumentIssuerType issuer = DocumentIssuerType.Own, InvoiceStatus status = InvoiceStatus.Pending,
        NfeStatus nfeStatus = NfeStatus.None, string? accessKey = null, string number = "000000101")
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Depositor, IssuerType = issuer, InvoiceStatus = status,
            NfeStatus = nfeStatus, ChaveNFe = accessKey, InvoiceNumber = "DE" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = number, TaxDocumentSeries = "1", IssueDate = DateTime.Now.Date,
            CustodyStorageTransactionKey = transaction.Key, CustodyRole = role,
            Items = [new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = UsageOf(role), Cfop = role == StorageCustodyRole.ProcessingEntry ? "1901" : "1905",
            }],
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>Nota de custódia de SAÍDA já gravada (sem passar pela guarda).</summary>
    public async Task<SalesInvoice> AddSalesDocumentAsync(
        StorageTransaction transaction, StorageCustodyRole role, decimal quantity,
        InvoiceStatus status = InvoiceStatus.Pending, NfeStatus nfeStatus = NfeStatus.None, string number = "000000201")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Depositor, InvoiceStatus = status, NfeStatus = nfeStatus,
            InvoiceNumber = "DS" + Guid.NewGuid().ToString("N")[..6], TaxDocumentNumber = number, TaxDocumentSeries = "1",
            InvoiceDate = DateTime.Now.Date, CustodyStorageTransactionKey = transaction.Key, CustodyRole = role,
            Items = [new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = UsageOf(role),
            }],
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }
}
