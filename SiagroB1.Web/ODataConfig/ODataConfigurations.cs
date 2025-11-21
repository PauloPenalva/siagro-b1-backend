using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.ODataConfig;

public static class ODataConfigurations
{
    public static void ConfigureODataEntities(this ODataConventionModelBuilder modelBuilder)
    {
        modelBuilder.EntitySet<Branch>("Branchs");
        modelBuilder.EntitySet<UnitOfMeasure>("UnitsOfMeasure");
        modelBuilder.EntitySet<ProcessingService>("ProcessingServices");
        modelBuilder.EntitySet<QualityAttrib>("QualityAttribs");
        modelBuilder.EntitySet<ProcessingCost>("ProcessingCosts");
        modelBuilder.EntitySet<ProcessingCostDryingParameter>("ProcessingCostDryingParameters");
        modelBuilder.EntitySet<ProcessingCostDryingDetail>("ProcessingCostDryingDetails");
        modelBuilder.EntitySet<ProcessingCostQualityParameter>("ProcessingCostQualityParameters");
        modelBuilder.EntitySet<ProcessingCostServiceDetail>("ProcessingCostServiceDetails");
        modelBuilder.EntitySet<Warehouse>("Warehouses");
        modelBuilder.EntitySet<StorageLot>("StorageLots");
        modelBuilder.EntitySet<HarvestSeason>("HarvestSeasons");
        modelBuilder.EntitySet<TruckDriver>("TruckDrivers");
        modelBuilder.EntitySet<State>("States");
        modelBuilder.EntitySet<Truck>("Trucks");
        modelBuilder.EntitySet<WeighingTicket>("WeighingTickets");
        modelBuilder.EntitySet<PurchaseContractPriceFixation>("PurchaseContractsPriceFixations");
        modelBuilder.EntitySet<PurchaseContractTax>("PurchaseContractsTaxes");
        modelBuilder.EntitySet<PurchaseContractQualityParameter>("PurchaseContractsQualityParameters");
        modelBuilder.EntitySet<Tax>("Taxes");
        modelBuilder.EntitySet<StorageAddress>("StorageAddresses");
        modelBuilder.EntitySet<StorageTransaction>("StorageTransactions");
        modelBuilder.EntitySet<LogisticRegion>("LogisticRegions");
        modelBuilder.EntitySet<SalesContract>("SalesContracts");
        
        var purchaseContract = modelBuilder.EntitySet<PurchaseContract>("PurchaseContracts");
        purchaseContract.EntityType
            .Action("Approval")
            .Parameter<Guid>("key");
        
        purchaseContract.EntityType
            .Action("Cancel")
            .Parameter<Guid>("key");

        purchaseContract.EntityType
            .Action("Totals")
            .Returns<PurchaseContractTotalsDto>()
            .Parameter<Guid>("key");

        var shipmentReleases = modelBuilder.EntitySet<ShipmentRelease>("ShipmentReleases");
        shipmentReleases.EntityType
            .Action("Approvation")
            .Parameter<Guid>("key");
        
        shipmentReleases.EntityType
            .Action("Cancelation")
            .Parameter<Guid>("key");
    }
}