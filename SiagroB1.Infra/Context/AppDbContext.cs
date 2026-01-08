using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Infra.Context;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<State> States { get; set; }
    public DbSet<Branch> Branchs { get; set; }
    public DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; }
    public DbSet<ProcessingService> WhareHouseServices { get; set; }
    public DbSet<QualityAttrib> QualityAttribs { get; set; }
    public DbSet<ProcessingCost> ProcessingCosts { get; set; }
    public DbSet<ProcessingCostDryingParameter> ProcessingCostDryingParameters { get; set; }
    public DbSet<ProcessingCostDryingDetail> ProcessingCostDryingDetails { get; set; }
    public DbSet<ProcessingCostQualityParameter> ProcessingCostQualityParameters { get; set; }
    public DbSet<ProcessingCostServiceDetail> ProcessingCostServiceDetails { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<HarvestSeason> HarvestSeasons { get; set; }
    public DbSet<TruckDriver> TruckDrivers { get; set; }
    public DbSet<Truck> Trucks { get; set; }
    public DbSet<WeighingTicket> WeighingTickets { get; set; }
    public DbSet<QualityInspection> QualityInspections { get; set; }
    public DbSet<PurchaseContract> PurchaseContracts { get; set; }
    public DbSet<PurchaseContractPriceFixation> PurchaseContractsPriceFixations { get; set; }
    public DbSet<PurchaseContractTax> PurchaseContractsTaxes { get; set; }
    public DbSet<PurchaseContractBroker> PurchaseContractsBrokers { get; set; }
    public DbSet<PurchaseContractQualityParameter> PurchaseContractsQualityParameters { get; set; }
    public DbSet<PurchaseContractAllocation> PurchaseContractsAllocations { get; set; }
    public DbSet<Tax> Taxes { get; set; }
    public DbSet<ShipmentRelease> ShipmentReleases { get; set; }
    public DbSet<StorageAddress> StorageAddresses { get; set; }
    public DbSet<StorageTransaction> StorageTransactions { get; set; }
    public DbSet<StorageTransactionQualityInspection> StorageTransactionQualityInspections { get; set; }
    public DbSet<LogisticRegion> LogisticRegions { get; set; }
    public DbSet<SalesContract> SalesContracts { get; set; }
    public DbSet<ShippingOrder> ShippingOrders { get; set; }
    public DbSet<SalesInvoice> SalesInvoices { get; set; }
    public DbSet<SalesInvoiceItem> SalesInvoicesItems { get; set; }
    public DbSet<Agent> Agents { get; set; }
    public DbSet<ShippingTransaction> ShippingTransactions { get; set; }
    public DbSet<DocNumber> DocNumbers { get; set; }
    
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurar todas as relações para NoAction
        foreach (var relationship in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }
}
    