using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

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
    public DbSet<PurchaseContractChangeLog> PurchaseContractsChangeLogs { get; set; }
    public DbSet<PurchaseContractComment> PurchaseContractsComments { get; set; }
    public DbSet<SalesContractAllocation> SalesContractsAllocations { get; set; }
    public DbSet<Tax> Taxes { get; set; }
    public DbSet<ShipmentRelease> ShipmentReleases { get; set; }
    public DbSet<SalesShipmentRelease> SalesShipmentReleases { get; set; }
    public DbSet<StorageAddress> StorageAddresses { get; set; }
    public DbSet<StorageTransaction> StorageTransactions { get; set; }
    public DbSet<StorageTransactionQualityInspection> StorageTransactionQualityInspections { get; set; }
    public DbSet<LogisticRegion> LogisticRegions { get; set; }
    public DbSet<CostCenter> CostCenters { get; set; }
    public DbSet<LedgerAccount> LedgerAccounts { get; set; }
    public DbSet<Usage> Usages { get; set; }
    public DbSet<UsageEffect> UsageEffects { get; set; }
    public DbSet<CustomerReturn> CustomerReturns { get; set; }
    public DbSet<CustomerReturnItem> CustomerReturnsItems { get; set; }
    public DbSet<SalesContract> SalesContracts { get; set; }
    public DbSet<SalesContractPriceFixation> SalesContractsPriceFixations { get; set; }
    public DbSet<SalesContractDeliveryLocation> SalesContractsDeliveryLocations { get; set; }
    public DbSet<SalesContractChangeLog> SalesContractsChangeLogs { get; set; }
    public DbSet<SalesContractComment> SalesContractsComments { get; set; }
    public DbSet<ShippingOrder> ShippingOrders { get; set; }
    public DbSet<SalesInvoice> SalesInvoices { get; set; }
    public DbSet<SalesInvoiceItem> SalesInvoicesItems { get; set; }
    public DbSet<SalesInvoiceChangeLog> SalesInvoicesChangeLogs { get; set; }
    public DbSet<SalesInvoiceComment> SalesInvoicesComments { get; set; }
    public DbSet<Agent> Agents { get; set; }
    public DbSet<ShippingTransaction> ShippingTransactions { get; set; }
    public DbSet<StorageEntryTransaction> StorageEntryTransactions { get; set; }
    public DbSet<DocNumber> DocNumbers { get; set; }
    public DbSet<OwnershipTransfer> OwnershipTransfers { get; set; }
    public DbSet<PurchaseContractAttachment>  PurchaseContractAttachments { get; set; }
    public DbSet<SalesContractAttachment>  SalesContractAttachments { get; set; }
    
    public DbSet<StorageCharge> StorageCharges { get; set; }
    
    public DbSet<StorageDailyBalance> StorageDailyBalances { get; set; }
    
    public DbSet<StorageInvoice> StorageInvoices { get; set; }
    
    public DbSet<SystemSetup> SystemSetup { get; set; }
    
    public DbSet<Item> Items { get; set; }
    
    public DbSet<BusinessPartner>  BusinessPartners { get; set; }
    
    public DbSet<Address> Addresses { get; set; }
    public DbSet<TruckScale> TruckScales { get; set; }

    public DbSet<NotificationGroup> NotificationGroups { get; set; }
    public DbSet<NotificationGroupMember> NotificationGroupMembers { get; set; }
    public DbSet<NotificationGroupSubscription> NotificationGroupSubscriptions { get; set; }
    public DbSet<NotificationOutboxMessage> NotificationOutboxMessages { get; set; }
    public DbSet<NotificationDeliveryLog> NotificationDeliveryLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurar todas as relações para NoAction
        foreach (var relationship in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.NoAction;
        }
        
        modelBuilder.Entity<StorageInvoice>()
            .HasIndex(x => new { x.StorageAddressCode, x.PeriodStart, x.PeriodEnd })
            .IsUnique()
            .HasFilter($"[Status] <> {(int)StorageInvoiceStatus.Cancelled}");

        // Invariante "uma transferência de titularidade, uma liberação de embarque",
        // no banco. As outras duas camadas são a guarda de status no confirm e a
        // guarda inversa nos serviços de liberação; esta é a que sobrevive a
        // qualquer caminho novo. Filtrado porque a esmagadora maioria das
        // liberações é comum e tem a coluna nula.
        modelBuilder.Entity<ShipmentRelease>()
            .HasIndex(x => x.OwnershipTransferKey)
            .IsUnique()
            .HasFilter("[OwnershipTransferKey] IS NOT NULL");
        
        // Diferença entre entregue e faturado, mantida pelo próprio SQL Server. Coluna real
        // (ordena, filtra e sai em relatório), mas derivada: assim não depende de nenhum dos
        // serviços que escrevem Quantity/DeliveredQuantity lembrarem de atualizá-la.
        // Entrega ainda não conferida (zerada e em aberto) tem diferença 0, não a quantidade
        // inteira negativa: ali não há divergência apurada, só falta digitar.
        // A precisão precisa ser explícita: sem ela o EF assume decimal(18,2) e a terceira
        // casa das quantidades (ambas DECIMAL(18,3)) seria truncada silenciosamente.
        modelBuilder.Entity<SalesInvoiceItem>()
            .Property(i => i.DeliveryDifference)
            .HasColumnType("DECIMAL(18,3)")
            .HasComputedColumnSql(
                "CASE WHEN [DeliveredQuantity] = 0 " +
                $"AND [DeliveryStatus] = {(int)SalesInvoiceDeliveryStatus.Open} THEN 0 " +
                "ELSE [DeliveredQuantity] - [Quantity] END",
                stored: true);

        // Uma chave de NF-e do cliente, uma devolução registrada. Filtrado porque cancelar
        // precisa liberar a chave para o relançamento — mesma trava já usada no documento de
        // saída para a NF-e própria.
        modelBuilder.Entity<CustomerReturn>()
            .HasIndex(x => x.AccessKey)
            .IsUnique()
            .HasFilter($"[AccessKey] IS NOT NULL AND [Status] <> {(int)CustomerReturnStatus.Cancelled}");

        modelBuilder.Entity<Address>()
            .HasKey(a => new { a.CardCode, a.AddressName, a.AdresType });

        modelBuilder.Entity<Address>()
            .HasOne(a => a.BusinessPartner)
            .WithMany(bp => bp.Addresses)
            .HasForeignKey(a => a.CardCode);
    }
}
    