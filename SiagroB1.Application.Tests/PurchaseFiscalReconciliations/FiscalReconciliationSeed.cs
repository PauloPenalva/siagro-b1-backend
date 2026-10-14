using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>Fornecedor F0001 (CPF 12345678901), produto SOJA, romaneios e NFs de entrada para a conciliação.</summary>
internal sealed class FiscalReconciliationSeed
{
    public const string Supplier = "F0001";
    public const string Product = "SOJA";
    public UnitOfWork Db { get; }
    private int _seq;

    private FiscalReconciliationSeed(UnitOfWork db) => Db = db;

    /// <summary>Serviço real da conciliação, com o sync do firme (spec 2026-10-13, títulos firmes).</summary>
    public static PurchaseFiscalReconciliationService NewService(
        UnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE") =>
        new(db, FinancialDocumentTestServices.FirmSync(db, numbers, erp));

    public static async Task<FiscalReconciliationSeed> CreateAsync(UnitOfWork? db = null)
    {
        var seed = new FiscalReconciliationSeed(db ?? TestDb.CreateUnitOfWork());
        await seed.AddPartnerAsync(Supplier, "Produtor Fazenda A", "12345678901");
        return seed;
    }

    public async Task AddPartnerAsync(string cardCode, string name, string? taxId)
    {
        Db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = cardCode, CardName = name, TaxId = taxId, CardType = "S" });
        await Db.SaveChangesAsync();
    }

    public async Task<PurchaseContract> AddContractAsync(string code = "PC-1")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = Supplier, ItemCode = Product, UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "25/26", Status = ContractStatus.Approved, TotalVolume = 1_000_000m,
            FreightTerms = FreightTerms.Cif, DeliveryLocationCode = "01",
        };
        Db.Context.PurchaseContracts.Add(contract);
        await Db.SaveChangesAsync();
        return contract;
    }

    public async Task<StorageTransaction> AddTransactionAsync(
        decimal net, StorageTransactionType type = StorageTransactionType.Purchase, PurchaseContract? contract = null,
        DateTime? date = null, string? code = null, string cardCode = Supplier,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        var tx = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code ?? $"RM{++_seq:000000}", BranchCode = "01", CardCode = cardCode,
            ItemCode = Product, UnitOfMeasureCode = "KG", WarehouseCode = "PORTO", TruckCode = "ABC1D23",
            TransactionType = type, TransactionStatus = status, TransactionDate = date ?? new DateTime(2026, 10, 10),
            GrossWeight = net, NetWeight = net,
        };
        Db.Context.StorageTransactions.Add(tx);
        if (contract != null)
            Db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
                { Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, StorageTransactionKey = tx.Key, Volume = net });
        await Db.SaveChangesAsync();
        return tx;
    }

    public async Task<(PurchaseInvoice Invoice, PurchaseInvoiceItem Line)> AddInvoiceAsync(
        decimal quantity, string unit = "KG", PurchaseInvoiceType type = PurchaseInvoiceType.Normal,
        string cardCode = Supplier, PurchaseContract? contract = null, DateTime? issueDate = null,
        InvoiceStatus status = InvoiceStatus.Confirmed, string? taxNumber = null, Guid? producerInvoiceKey = null,
        string itemCode = Product, Guid? custodyTransactionKey = null,
        DocumentIssuerType issuerType = DocumentIssuerType.ThirdParty)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = cardCode, CardName = cardCode, InvoiceType = type,
            InvoiceStatus = status, InvoiceNumber = $"DE{++_seq:000000}", TaxDocumentNumber = taxNumber ?? $"{_seq:000000000}",
            TaxDocumentSeries = "1", IssueDate = issueDate ?? new DateTime(2026, 10, 9), ProducerInvoiceKey = producerInvoiceKey,
            CustodyStorageTransactionKey = custodyTransactionKey, IssuerType = issuerType,
        };
        var line = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = itemCode, ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = unit, Quantity = quantity, UnitPrice = 2m, PurchaseContractKey = contract?.Key,
            UsageName = "Compra para comercialização",
        };
        Db.Context.PurchaseInvoices.Add(invoice);
        Db.Context.PurchaseInvoicesItems.Add(line);
        await Db.SaveChangesAsync();
        return (invoice, line);
    }

    public async Task<PurchaseFiscalReconciliation> LinkAsync(PurchaseInvoiceItem line, StorageTransaction tx, decimal quantity)
    {
        var link = new PurchaseFiscalReconciliation
            { Key = Guid.NewGuid(), PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = tx.Key, Quantity = quantity };
        Db.Context.PurchaseFiscalReconciliations.Add(link);
        await Db.SaveChangesAsync();
        return link;
    }
}
