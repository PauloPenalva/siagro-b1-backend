using Microsoft.AspNetCore.Mvc;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Models;

namespace SiagroB1.Web.ODataConfig;

public static class ODataConfigurations
{
    public static void ConfigureODataEntities(this ODataConventionModelBuilder modelBuilder)
    {
        modelBuilder.EntitySet<BusinessPartner>("BusinessPartners");
        modelBuilder.EntitySet<Item>("Items");
        modelBuilder.EntitySet<Company>("Companies");
        modelBuilder.EntitySet<Branch>("Branchs");
        modelBuilder.EntitySet<UnitOfMeasureModel>("UnitsOfMeasure");
        modelBuilder.EntitySet<ProcessingService>("ProcessingServices");
        modelBuilder.EntitySet<QualityAttrib>("QualityAttribs");
        modelBuilder.EntitySet<ProcessingCost>("ProcessingCosts");
        modelBuilder.EntitySet<ProcessingCostDryingParameter>("ProcessingCostDryingParameters");
        modelBuilder.EntitySet<ProcessingCostDryingDetail>("ProcessingCostDryingDetails");
        modelBuilder.EntitySet<ProcessingCostQualityParameter>("ProcessingCostQualityParameters");
        modelBuilder.EntitySet<ProcessingCostServiceDetail>("ProcessingCostServiceDetails");
        modelBuilder.EntitySet<WarehouseModel>("Warehouses");
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
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(StorageAddress))
            .AddProperty(typeof(StorageAddress).GetProperty(nameof(StorageAddress.Balance)));
        modelBuilder.EntitySet<StorageTransaction>("StorageTransactions");
        modelBuilder.EntitySet<StorageTransactionQualityInspection>("StorageTransactionsQualityInspections");
        modelBuilder.EntitySet<LogisticRegion>("LogisticRegions");
        modelBuilder.EntitySet<PurchaseContract>("PurchaseContracts");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseContract))
            .AddProperty(typeof(PurchaseContract).GetProperty(nameof(PurchaseContract.TotalStandard)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseContract))
            .AddProperty(typeof(PurchaseContract).GetProperty(nameof(PurchaseContract.TotalAvailableToRelease)));
        modelBuilder.EntitySet<SalesContract>("SalesContracts");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesContract))
            .AddProperty(typeof(SalesContract).GetProperty(nameof(SalesContract.TotalPrice)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesContract))
            .AddProperty(typeof(SalesContract).GetProperty(nameof(SalesContract.AvaiableVolume)));
        modelBuilder.EntitySet<AgentModel>("Agents");
        modelBuilder.EntitySet<ShipmentRelease>("ShipmentReleases");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
            .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.AvailableQuantity)));
        modelBuilder.EntitySet<SalesInvoice>("SalesInvoices");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
            .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalInvoiceItems)));
        modelBuilder.EntitySet<SalesInvoiceItem>("SalesInvoicesItems");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoiceItem))
            .AddProperty(typeof(SalesInvoiceItem).GetProperty(nameof(SalesInvoiceItem.Total)));
        modelBuilder.EntitySet<DocNumber>("DocNumbers");
        modelBuilder.EntitySet<WeighingTicket>("WeighingTickets");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(WeighingTicket))
            .AddProperty(typeof(WeighingTicket).GetProperty(nameof(WeighingTicket.GrossWeight)));
        modelBuilder.EntitySet<QualityInspection>("WeighingTicketsQualityInspections");
        
        var shippingTransactionCreate = modelBuilder.Action("ShippingTransactionsCreate");
        shippingTransactionCreate.Parameter<Guid>("PurchaseContractKey");
        shippingTransactionCreate.EntityParameter<StorageTransaction>("StorageTransaction");
        shippingTransactionCreate.Returns<IActionResult>();
        
        var storageTransactionsConfirmed = modelBuilder.Action("StorageTransactionsConfirmed");
        storageTransactionsConfirmed.Parameter<Guid>("Key");
        storageTransactionsConfirmed.Returns<IActionResult>();
        
        var storageTransactionsCancel = modelBuilder.Action("StorageTransactionsCancel");
        storageTransactionsCancel.Parameter<Guid>("Key");
        storageTransactionsCancel.Returns<IActionResult>();
        
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

        var salesContractsGetTotals = modelBuilder.Function("SalesContractsGetTotals");
        salesContractsGetTotals.Parameter<Guid>("key");
        salesContractsGetTotals.Returns<SalesContractTotalsResponseDto>();
        
        var storageAddressesTotal = modelBuilder.Function("StorageAddressesTotals");
        storageAddressesTotal.Parameter<string>("code");
        storageAddressesTotal.Returns<IActionResult>();
        
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
        shipmentReleasesApprovation.Parameter<Guid>("Key");
        shipmentReleasesApprovation.Returns<IActionResult>();
        
        var shipmentReleasesCancelation = modelBuilder.Action("ShipmentReleasesCancelation");
        shipmentReleasesCancelation.Parameter<Guid>("Key");
        shipmentReleasesCancelation.Returns<IActionResult>();
        
        var shipmentReleasesBalance = modelBuilder.Function("ShipmentReleasesGetBalance");
        shipmentReleasesBalance.Parameter<string>("ItemCode");
        shipmentReleasesBalance.Returns<ICollection<ShipmentRelesesBalanceResponseDto>>();
        
        var shipmentBillingCreateSalesInvoice = 
            modelBuilder.Action("ShipmentBillingCreateSalesInvoice");
        shipmentBillingCreateSalesInvoice.EntityParameter<SalesInvoice>("SalesInvoice");
        shipmentBillingCreateSalesInvoice.Returns<IActionResult>();
        
        var shipmentBillingDelete = modelBuilder.Action("ShipmentBillingDeleteInvoice");
        shipmentBillingDelete.Parameter<Guid>("Key");
        shipmentBillingDelete.Returns<IActionResult>();

        var salesInvoicesCancel = modelBuilder.Action("SalesInvoicesCancel");
        salesInvoicesCancel.Parameter<Guid>("Key");
        salesInvoicesCancel.Returns<IActionResult>();
        
        var salesInvoicesReturn = modelBuilder.Action("SalesInvoicesReturn");
        salesInvoicesReturn.Parameter<Guid>("Key");
        salesInvoicesReturn.Returns<IActionResult>();
        
        var salesInvoicesConfirm = modelBuilder.Action("SalesInvoicesConfirm");
        salesInvoicesConfirm.Parameter<Guid>("Key");
        salesInvoicesConfirm.Returns<IActionResult>();
        
        var weighingTicketsFirstWeighing = modelBuilder.Action("WeighingTicketsFirstWeighing");
        weighingTicketsFirstWeighing.Parameter<Guid>("Key");
        weighingTicketsFirstWeighing.Parameter<int>("Value");
        weighingTicketsFirstWeighing.Returns<IActionResult>();
        
        var weighingTicketsSecondWeighing = modelBuilder.Action("WeighingTicketsSecondWeighing");
        weighingTicketsSecondWeighing.Parameter<Guid>("Key");
        weighingTicketsSecondWeighing.Parameter<int>("Value");
        weighingTicketsSecondWeighing.Returns<IActionResult>();

        var weighingTicketsCompleted = modelBuilder.Action("WeighingTicketsCompleted");
        weighingTicketsCompleted.Parameter<Guid>("Key");
        weighingTicketsCompleted.Returns<IActionResult>();
        
        var weighingTicketsCancel = modelBuilder.Action("WeighingTicketsCancel");
        weighingTicketsCancel.Parameter<Guid>("Key");
        weighingTicketsCancel.Returns<IActionResult>();
        
        
        
        
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
        
    }
}