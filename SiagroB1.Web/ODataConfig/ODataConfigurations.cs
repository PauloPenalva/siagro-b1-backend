using Microsoft.AspNetCore.Mvc;
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
        modelBuilder.EntitySet<HarvestSeason>("HarvestSeasons");
        modelBuilder.EntitySet<TruckDriver>("TruckDrivers");
        modelBuilder.EntitySet<State>("States");
        modelBuilder.EntitySet<Truck>("Trucks");
        modelBuilder.EntitySet<PurchaseContractPriceFixation>("PurchaseContractsPriceFixations");
        modelBuilder.EntitySet<PurchaseContractTax>("PurchaseContractsTaxes");
        modelBuilder.EntitySet<PurchaseContractBroker>("PurchaseContractsBrokers");
        modelBuilder.EntitySet<PurchaseContractQualityParameter>("PurchaseContractsQualityParameters");
        modelBuilder.EntitySet<PurchaseContractAllocation>("PurchaseContractsAllocations");
        modelBuilder.EntitySet<Tax>("Taxes");
        modelBuilder.EntitySet<StorageAddress>("StorageAddresses");
        modelBuilder.EntitySet<StorageTransaction>("StorageTransactions");
        modelBuilder.EntitySet<StorageTransactionQualityInspection>("StorageTransactionsQualityInspections");
        modelBuilder.EntitySet<LogisticRegion>("LogisticRegions");
        modelBuilder.EntitySet<PurchaseContract>("PurchaseContracts");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseContract))
            .AddProperty(typeof(PurchaseContract).GetProperty(nameof(PurchaseContract.TotalStandard)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseContract))
            .AddProperty(typeof(PurchaseContract).GetProperty(nameof(PurchaseContract.TotalAvailableToRelease)));
        
        modelBuilder.EntitySet<SalesContract>("SalesContracts");
        modelBuilder.EntitySet<DocType>("DocTypes");
        modelBuilder.EntitySet<Agent>("Agents");
        
        modelBuilder.EntitySet<ShipmentRelease>("ShipmentReleases");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
            .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.AvailableQuantity)));
        
        var shippingTransactionCreate = modelBuilder.Action("ShippingTransactionsCreate");
        shippingTransactionCreate.Parameter<Guid>("PurchaseContractKey");
        shippingTransactionCreate.EntityParameter<StorageTransaction>("StorageTransaction");
        shippingTransactionCreate.Returns<IActionResult>();
        
        var storageTransactionsConfirmed = modelBuilder.Action("StorageTransactionsConfirmed");
        storageTransactionsConfirmed.Parameter<Guid>("Key");
        storageTransactionsConfirmed.Returns<IActionResult>();
        
        var storageTransactionsCopy = modelBuilder.Action("StorageTransactionsCopy");
        storageTransactionsCopy.Parameter<Guid>("Key");
        storageTransactionsCopy.Returns<IActionResult>();
        
        var salesContractCopy = modelBuilder.Action("SalesContractsCopy");
        salesContractCopy.Parameter<Guid>("Key");
        salesContractCopy.Returns<IActionResult>();
        
        var salesContractsWithdrawApproval =  modelBuilder.Action("SalesContractsWithdrawApproval");
        salesContractsWithdrawApproval.Parameter<Guid>("Key");
        salesContractsWithdrawApproval.Returns<IActionResult>();
        
        var salesContractsSendApproval =  modelBuilder.Action("SalesContractsSendApproval");
        salesContractsSendApproval.Parameter<Guid>("Key");
        salesContractsSendApproval.Returns<IActionResult>();
        
        var salesContractsApproval = modelBuilder.Action("SalesContractsApproval");
        salesContractsApproval.Parameter<Guid>("Key");
        salesContractsApproval.Parameter<string>("Comments");
        salesContractsApproval.Returns<IActionResult>();
        
        var salesContractsReject = modelBuilder.Action("SalesContractsReject");
        salesContractsReject.Parameter<Guid>("Key");
        salesContractsReject.Parameter<string>("Comments");
        salesContractsReject.Returns<IActionResult>();

        var salesContractsGetTotals = modelBuilder.Function("SalesContractsGetTotals")
            .Returns<SalesContractTotalsResponseDto>()
            .Parameter<Guid>("key");
        
        var storageAddresses = modelBuilder.EntitySet<StorageAddress>("StorageAddresses");
        storageAddresses.EntityType
            .Action("Totals")
            .Parameter<Guid>("key");
        
        var purchaseContractsGetAvaiablesList = modelBuilder.Function("PurchaseContractsGetAvaiablesList");
        purchaseContractsGetAvaiablesList.Parameter<string>("CardCode");
        purchaseContractsGetAvaiablesList.Parameter<string>("ItemCode");
        purchaseContractsGetAvaiablesList.Returns<ICollection<PurchaseContractDto>>();

        var purchaseContractsGetShipmentReleasesAvailable = modelBuilder.Function("PurchaseContractsGetShipmentReleasesAvailable");
        purchaseContractsGetShipmentReleasesAvailable.Returns<PurchaseContract>();
        
        var purchaseContractsApproval = modelBuilder.Action("PurchaseContractsApproval");
        purchaseContractsApproval.Parameter<Guid>("Key");
        purchaseContractsApproval.Parameter<string>("Comments");
        purchaseContractsApproval.Returns<IActionResult>();
        
        var purchaseContractsReject = modelBuilder.Action("PurchaseContractsReject");
        purchaseContractsReject.Parameter<Guid>("Key");
        purchaseContractsReject.Parameter<string>("Comments");
        purchaseContractsReject.Returns<IActionResult>();
        
        var purchaseContractsCreateAllocation = modelBuilder.Action("PurchaseContractsCreateAllocation");
        purchaseContractsCreateAllocation.Parameter<Guid>("PurchaseContractKey");
        purchaseContractsCreateAllocation.Parameter<Guid>("StorageTransactionKey");
        purchaseContractsCreateAllocation.Parameter<decimal>("Volume");
        purchaseContractsCreateAllocation.Returns<IActionResult>();
        
        var purchaseContractsDeleteAllocation = modelBuilder.Action("PurchaseContractsDeleteAllocation");
        purchaseContractsDeleteAllocation.Parameter<Guid>("Key");
        purchaseContractsDeleteAllocation.Returns<IActionResult>();
            
        var shipmentReleasesApprovation = modelBuilder.Action("ShipmentReleasesApprovation");
        shipmentReleasesApprovation.Parameter<int>("RowId");
        shipmentReleasesApprovation.Returns<IActionResult>();
        
        var shipmentReleasesCancelation = modelBuilder.Action("ShipmentReleasesCancelation");
        shipmentReleasesCancelation.Parameter<int>("RowId");
        shipmentReleasesCancelation.Returns<IActionResult>();
        
        var shipmentReleasesBalance = modelBuilder.Function("ShipmentReleasesGetBalance");
        shipmentReleasesBalance.Parameter<string>("ItemCode");
        shipmentReleasesBalance.Returns<ICollection<ShipmentRelesesBalanceResponseDto>>();
        
        
        
        
        
        
        
        
        
        
        
        
        
        modelBuilder
            .Action("PurchaseContractsTotals")
            .Returns<PurchaseContractTotalsResponseDto>()
            .Parameter<Guid>("Key");
        
        modelBuilder
            .Action("PurchaseContractsWithdrawApproval")
            .Parameter<Guid>("key");
        
        modelBuilder
            .Action("PurchaseContractsSendApproval")
            .Parameter<Guid>("key"); 
        
        modelBuilder
            .Action("PurchaseContractsCopy")
            .Parameter<Guid>("key");


       
        
        
        
        
        
        
        
        
        var shippingOrders  = modelBuilder.EntitySet<ShippingOrder>("ShippingOrders");
        shippingOrders.EntityType
            .Action("Shipped")
            .Parameter<Guid>("key");
        
        shippingOrders.EntityType
            .Action("Cancel")
            .Parameter<Guid>("key");
        
        var weighingTickets = modelBuilder.EntitySet<WeighingTicket>("WeighingTickets");
        weighingTickets.EntityType
            .Action("FirstWeighing")
            .Parameter<Guid>("key");
        
        weighingTickets.EntityType
            .Action("SecondWeighing")
            .Parameter<Guid>("key");
        
        weighingTickets.EntityType
            .Action("Completed")
            .Parameter<Guid>("key");
        
        weighingTickets.EntityType
            .Action("Cancel")
            .Parameter<Guid>("key");
        
        weighingTickets.EntityType
            .Action("QualityInspections")
            .Parameter<Guid>("key");
    }
}