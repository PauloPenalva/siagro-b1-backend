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
    public DbSet<PurchaseInvoice> PurchaseInvoices { get; set; }
    public DbSet<PurchaseInvoiceItem> PurchaseInvoicesItems { get; set; }
    public DbSet<PurchaseInvoiceChangeLog> PurchaseInvoicesChangeLogs { get; set; }
    public DbSet<PurchaseInvoiceComment> PurchaseInvoicesComments { get; set; }
    public DbSet<Agent> Agents { get; set; }
    public DbSet<ShippingTransaction> ShippingTransactions { get; set; }
    public DbSet<StorageEntryTransaction> StorageEntryTransactions { get; set; }
    public DbSet<DocNumber> DocNumbers { get; set; }
    public DbSet<OwnershipTransfer> OwnershipTransfers { get; set; }
    public DbSet<ShipmentLoad> ShipmentLoads { get; set; }
    public DbSet<ShipmentLoadMovement> ShipmentLoadMovements { get; set; }
    public DbSet<PurchaseContractAttachment>  PurchaseContractAttachments { get; set; }
    public DbSet<SalesContractAttachment>  SalesContractAttachments { get; set; }
    
    public DbSet<StorageCharge> StorageCharges { get; set; }
    
    public DbSet<StorageDailyBalance> StorageDailyBalances { get; set; }
    
    public DbSet<StorageInvoice> StorageInvoices { get; set; }
    
    public DbSet<SystemSetup> SystemSetup { get; set; }
    
    public DbSet<Item> Items { get; set; }

    public DbSet<ItemComplement> ItemComplements { get; set; }

    public DbSet<WarehouseComplement> WarehouseComplements { get; set; }
    
    public DbSet<BusinessPartner>  BusinessPartners { get; set; }
    
    public DbSet<Address> Addresses { get; set; }
    public DbSet<TruckScale> TruckScales { get; set; }
    public DbSet<UserTruckScale> UserTruckScales { get; set; }

    public DbSet<NotificationGroup> NotificationGroups { get; set; }
    public DbSet<NotificationGroupMember> NotificationGroupMembers { get; set; }
    public DbSet<NotificationGroupSubscription> NotificationGroupSubscriptions { get; set; }
    public DbSet<NotificationOutboxMessage> NotificationOutboxMessages { get; set; }
    public DbSet<NotificationDeliveryLog> NotificationDeliveryLogs { get; set; }

    public DbSet<FinancialAccount> FinancialAccounts { get; set; }
    public DbSet<FinancialDocument> FinancialDocuments { get; set; }
    public DbSet<FinancialSettlement> FinancialSettlements { get; set; }
    public DbSet<FinancialDocumentChangeLog> FinancialDocumentChangeLogs { get; set; }

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

        // Invariante "um dono da diferença de entrega por item de nota", no banco. A regra é
        // mantida por SalesContractsDeliveryDifferenceOwnerService em todo caminho que mexe no
        // ledger; este índice é o que sobrevive a um caminho novo que esqueça de chamá-la —
        // dois donos passariam despercebidos e fariam a quebra ser descontada em dobro.
        // Filtrado porque a esmagadora maioria das linhas NÃO é dona (só uma por item é).
        modelBuilder.Entity<SalesContractAllocation>()
            .HasIndex(x => x.SalesInvoiceItemKey, "IX_SALES_CONTRACTS_ALLOCATIONS_DeliveryDifferenceOwner")
            .IsUnique()
            .HasFilter("[OwnsDeliveryDifference] = 1");

        // Uma chave de NF-e, um documento de entrada registrado. Filtrado porque cancelar precisa
        // LIBERAR a chave para o relançamento — mesma trava já usada no documento de saída.
        modelBuilder.Entity<PurchaseInvoice>()
            .HasIndex(x => x.ChaveNFe)
            .IsUnique()
            .HasFilter($"[ChaveNFe] IS NOT NULL AND [InvoiceStatus] <> {(int)InvoiceStatus.Cancelled}");

        // Auto-relação: a NF de remessa aponta a NF de venda futura que a antecipou. Restrict, e
        // não Cascade — apagar a nota futura não pode levar as remessas junto.
        modelBuilder.Entity<PurchaseInvoice>()
            .HasOne(x => x.PurchaseInvoiceOrigin)
            .WithMany()
            .HasForeignKey(x => x.PurchaseInvoiceOriginKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseInvoiceItem>()
            .HasOne(x => x.PurchaseInvoiceItemOrigin)
            .WithMany()
            .HasForeignKey(x => x.PurchaseInvoiceItemOriginKey)
            .OnDelete(DeleteBehavior.Restrict);

        // FK OPCIONAL de propósito: entrada NORMAL não tem origem de saída, e uma FK obrigatória
        // viraria INNER JOIN zerando a coleção inteira.
        modelBuilder.Entity<PurchaseInvoiceItem>()
            .HasOne(x => x.SalesInvoiceItem)
            .WithMany()
            .HasForeignKey(x => x.SalesInvoiceItemKey)
            .OnDelete(DeleteBehavior.Restrict);

        // FK OPCIONAL, como a de origem acima: entrada de insumo/serviço não tem contrato, e uma FK
        // obrigatória viraria INNER JOIN zerando a coleção inteira de itens.
        // Restrict porque apagar um contrato não pode apagar a linha fiscal que o referencia.
        modelBuilder.Entity<PurchaseInvoiceItem>()
            .HasOne(x => x.PurchaseContract)
            .WithMany()
            .HasForeignKey(x => x.PurchaseContractKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Address>()
            .HasKey(a => new { a.CardCode, a.AddressName, a.AdresType });

        modelBuilder.Entity<ItemComplement>()
            .HasKey(x => x.ItemCode);

        modelBuilder.Entity<WarehouseComplement>()
            .HasKey(x => x.WarehouseCode);

        modelBuilder.Entity<Address>()
            .HasOne(a => a.BusinessPartner)
            .WithMany(bp => bp.Addresses)
            .HasForeignKey(a => a.CardCode);

        // STORAGE_TRANSACTIONS aponta a carga por DUAS chaves de significados opostos:
        // ShipmentLoadKey é o romaneio MONTADO na carga (o que entrou) e
        // RefusedFromShipmentLoadKey é a devolução gerada pela RECUSA dela (o que voltou).
        // Com duas FKs para a mesma entidade e duas coleções inversas a convenção do EF não
        // tem como parear sozinha, e emparelharia errado em silêncio — o que faria
        // ShipmentLoadsRecalculateTotalService somar a devolução no volume embarcado.
        modelBuilder.Entity<StorageTransaction>()
            .HasOne(x => x.ShipmentLoad)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.ShipmentLoadKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StorageTransaction>()
            .HasOne(x => x.RefusedFromShipmentLoad)
            .WithMany(x => x.RefusalReturns)
            .HasForeignKey(x => x.RefusedFromShipmentLoadKey)
            .OnDelete(DeleteBehavior.NoAction);

        // Mesma armadilha do par acima, agora com SALES_INVOICES: SalesInvoiceKey é o romaneio
        // FATURADO na nota (o que ela consumiu) e GeneratedByReturnInvoiceKey é a devolução ao
        // armazém gerada pelo retorno dela (o que voltou). Duas navegações para a mesma entidade
        // deixam a convenção ambígua, e ela pode pendurar a segunda em SalesTransactions — o que
        // faria a devolução contar como embarque da nota. As duas são declaradas à mão por isso.
        modelBuilder.Entity<StorageTransaction>()
            .HasOne(x => x.SalesInvoice)
            .WithMany(x => x.SalesTransactions)
            .HasForeignKey(x => x.SalesInvoiceKey)
            .OnDelete(DeleteBehavior.NoAction);

        // Sem coleção inversa de propósito: a nota não precisa navegar para a devolução (quem
        // faz isso é a consulta por GeneratedByReturnInvoiceKey), e uma coleção a mais no
        // SalesInvoice entraria no EDM do OData sem ninguém pedir.
        modelBuilder.Entity<StorageTransaction>()
            .HasOne(x => x.GeneratedByReturnInvoice)
            .WithMany()
            .HasForeignKey(x => x.GeneratedByReturnInvoiceKey)
            .OnDelete(DeleteBehavior.NoAction);

        // STORAGE_TRANSACTIONS e SHIPMENT_RELEASES se apontam em DUAS direções de significados
        // opostos: StorageTransaction.ShipmentReleaseKey é "este romaneio CONSOME a liberação", e
        // ShipmentRelease.GeneratedByStorageTransactionKey é "esta liberação NASCEU deste romaneio"
        // (a devolução ao armazém). Mesma armadilha dos dois pares acima: sem as duas declaradas à
        // mão, a convenção pode pendurar a segunda em ShipmentRelease.Transactions — e o romaneio
        // de devolução passaria a contar como romaneio da liberação, fazendo o saldo dela nascer
        // NEGATIVO (o tipo 12 entra subtraindo no eixo de venda de CalculateShippedAsync).
        modelBuilder.Entity<StorageTransaction>()
            .HasOne(x => x.ShipmentRelease)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.ShipmentReleaseKey)
            .OnDelete(DeleteBehavior.NoAction);

        // Sem coleção inversa de propósito, como no par de SALES_INVOICES acima: o romaneio não
        // precisa navegar para a liberação que gerou, e uma coleção a mais entraria no EDM do
        // OData sem ninguém pedir. Não é única — uma devolução com romaneios de contratos
        // diferentes emite uma liberação por contrato.
        modelBuilder.Entity<ShipmentRelease>()
            .HasOne(x => x.GeneratedByStorageTransaction)
            .WithMany()
            .HasForeignKey(x => x.GeneratedByStorageTransactionKey)
            .OnDelete(DeleteBehavior.NoAction);

        // Duas navegações para contratos DIFERENTES, declaradas à mão porque a convenção
        // emparelha errado em silêncio. WithMany() SEM coleção inversa: uma coleção nova no
        // contrato entraria no EDM do OData sem ninguém pedir.
        modelBuilder.Entity<FinancialDocument>()
            .HasOne(x => x.PurchaseContract).WithMany()
            .HasForeignKey(x => x.PurchaseContractKey).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<FinancialDocument>()
            .HasOne(x => x.SalesContract).WithMany()
            .HasForeignKey(x => x.SalesContractKey).OnDelete(DeleteBehavior.NoAction);

        // A TRAVA DE IDEMPOTÊNCIA. Dois cliques em "Aprovar" gerariam dois provisórios
        // idênticos, e ninguém perceberia até o mês fechar com o dobro. Filtrado por
        // Provisional porque dois ADIANTAMENTOS no mesmo contrato são legítimos, e por
        // <> Canceled porque cancelar precisa LIBERAR a origem para a reabertura regenerar —
        // mesmo desenho do índice de PurchaseInvoice.ChaveNFe.
        modelBuilder.Entity<FinancialDocument>()
            .HasIndex(x => new { x.OriginType, x.OriginKey }, "IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin")
            .IsUnique()
            .HasFilter($"[Nature] = {(int)FinancialDocumentNature.Provisional} " +
                       $"AND [Status] <> {(int)FinancialDocumentStatus.Canceled} " +
                       "AND [OriginKey] IS NOT NULL");

        // Impede estornar a mesma baixa duas vezes.
        modelBuilder.Entity<FinancialSettlement>()
            .HasIndex(x => x.ReversedSettlementKey)
            .IsUnique()
            .HasFilter("[ReversedSettlementKey] IS NOT NULL");
    }
}
    