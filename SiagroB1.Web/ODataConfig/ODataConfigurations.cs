using Microsoft.AspNetCore.Mvc;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Models;

namespace SiagroB1.Web.ODataConfig;

public static class ODataConfigurations
{
    public static void ConfigureODataEntities(this ODataConventionModelBuilder modelBuilder)
    {
        modelBuilder.EntitySet<Company>("Companies");
        modelBuilder.EntitySet<Branch>("Branchs");
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
        modelBuilder.EntitySet<SalesContractAllocation>("SalesContractsAllocations");
        modelBuilder.EntitySet<PurchaseContractAttachment>("PurchaseContractsAttachments");

        modelBuilder.EntitySet<PurchaseContractChangeLog>("PurchaseContractsChangeLogs");
        modelBuilder.EntitySet<PurchaseContractComment>("PurchaseContractsComments");
        modelBuilder.EntitySet<Tax>("Taxes");
        modelBuilder.EntitySet<StorageAddress>("StorageAddresses");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(StorageAddress))
            .AddProperty(typeof(StorageAddress).GetProperty(nameof(StorageAddress.Balance)));
        modelBuilder.EntitySet<StorageTransaction>("StorageTransactions");
        modelBuilder.EntitySet<StorageEntryTransaction>("StorageEntryTransactions");
        modelBuilder.EntitySet<StorageTransactionQualityInspection>("StorageTransactionsQualityInspections");
        modelBuilder.EntitySet<LogisticRegion>("LogisticRegions");

        // notifications
        modelBuilder.EntitySet<NotificationGroup>("NotificationGroups");
        modelBuilder.EntitySet<NotificationGroupMember>("NotificationGroupMembers");
        modelBuilder.EntitySet<NotificationGroupSubscription>("NotificationGroupSubscriptions");
        modelBuilder.EntitySet<NotificationOutboxMessage>("NotificationOutboxMessages");
        modelBuilder.EntitySet<NotificationDeliveryLog>("NotificationDeliveryLogs");
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
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesContract))
            .AddProperty(typeof(SalesContract).GetProperty(nameof(SalesContract.TotalShipmentReleases)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesContract))
            .AddProperty(typeof(SalesContract).GetProperty(nameof(SalesContract.TotalAvailableToRelease)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesContract))
            .AddProperty(typeof(SalesContract).GetProperty(nameof(SalesContract.PhysicalAvailableToRelease)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesContract))
            .AddProperty(typeof(SalesContract).GetProperty(nameof(SalesContract.AvailableVolumeToPricing)));

        modelBuilder.EntitySet<SalesContractPriceFixation>("SalesContractsPriceFixations");

        modelBuilder.EntitySet<SalesContractDeliveryLocation>("SalesContractsDeliveryLocations");

        modelBuilder.EntitySet<SalesContractChangeLog>("SalesContractsChangeLogs");

        modelBuilder.EntitySet<SalesContractComment>("SalesContractsComments");

        modelBuilder.EntitySet<SalesContractAttachment>("SalesContractsAttachments");
        modelBuilder.EntitySet<ShipmentRelease>("ShipmentReleases");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
            .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.AvailableQuantity)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
            .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.ConsumedQuantity)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
            .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.ReturnedToContractQuantity)));
        modelBuilder.EntitySet<SalesShipmentRelease>("SalesShipmentReleases");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesShipmentRelease))
            .AddProperty(typeof(SalesShipmentRelease).GetProperty(nameof(SalesShipmentRelease.AvailableQuantity)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesShipmentRelease))
            .AddProperty(typeof(SalesShipmentRelease).GetProperty(nameof(SalesShipmentRelease.ConsumedQuantity)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesShipmentRelease))
            .AddProperty(typeof(SalesShipmentRelease).GetProperty(nameof(SalesShipmentRelease.ReturnedToContractQuantity)));
        modelBuilder.EntitySet<SalesInvoice>("SalesInvoices");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
            .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalInvoiceItems)));
        modelBuilder.EntitySet<SalesInvoiceItem>("SalesInvoicesItems");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoiceItem))
            .AddProperty(typeof(SalesInvoiceItem).GetProperty(nameof(SalesInvoiceItem.Total)));
        modelBuilder.EntitySet<SalesInvoiceChangeLog>("SalesInvoicesChangeLogs");
        modelBuilder.EntitySet<SalesInvoiceComment>("SalesInvoicesComments");
        modelBuilder.EntitySet<DocNumber>("DocNumbers");
        modelBuilder.EntitySet<WeighingTicket>("WeighingTickets");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(WeighingTicket))
            .AddProperty(typeof(WeighingTicket).GetProperty(nameof(WeighingTicket.GrossWeight)));
        modelBuilder.EntitySet<QualityInspection>("WeighingTicketsQualityInspections");
        modelBuilder.EntitySet<OwnershipTransfer>("OwnershipTransfers");
        modelBuilder.EntitySet<StorageInvoice>("StorageInvoices");
        modelBuilder.EntitySet<SystemSetup>("SystemSetup");
        modelBuilder.EntitySet<UnitOfMeasureModel>("UnitsOfMeasure");
        modelBuilder.EntitySet<ItemModel>("Items");
        modelBuilder.EntitySet<AgentModel>("Agents");
        modelBuilder.EntitySet<BusinessPartnerModel>("BusinessPartners");
        modelBuilder.EntityType<AddressModel>().HasKey(x => new { x.AddressName, x.AdresType, x.CardCode });
        modelBuilder.EntitySet<TruckScale>("TruckScales");
        modelBuilder.EntitySet<MenuItem>("MenuItems");
        modelBuilder.EntitySet<User>("Users");
        modelBuilder.EntitySet<UserProfile>("UsersProfiles");
        modelBuilder.EntitySet<Profile>("Profiles");
        modelBuilder.EntitySet<ProfileRole>("ProfilesRoles");
        modelBuilder.EntitySet<Role>("Roles");
        modelBuilder.EntitySet<RolePermission>("RolesPermissions");
        modelBuilder.EntitySet<RoleMenu>("RolesMenus");
        modelBuilder.EntitySet<Permission>("Permissions");
        
        var systemSetupGetActive = modelBuilder.Function("SystemSetupGetActive");
        systemSetupGetActive.Returns<IActionResult>();
        
        var docNumberGetInfoByTransaction = 
            modelBuilder.Function("DocNumberGetInfoByTransaction");
        docNumberGetInfoByTransaction.Parameter<string>("Transaction");
        docNumberGetInfoByTransaction.Returns<IActionResult>();
        
        var shippingTransactionCreate = modelBuilder.Action("ShippingTransactionsCreate");
        shippingTransactionCreate.Parameter<Guid>("PurchaseContractKey");
        shippingTransactionCreate.EntityParameter<StorageTransaction>("StorageTransaction");
        shippingTransactionCreate.Returns<IActionResult>();
        
        var storageEntryTransactionCreate = modelBuilder.Action("StorageEntryTransactionsCreate");
        storageEntryTransactionCreate.Parameter<Guid>("PurchaseContractKey");
        storageEntryTransactionCreate.Parameter<string>("StorageAddressCode");
        storageEntryTransactionCreate.EntityParameter<StorageTransaction>("StorageTransaction");
        storageEntryTransactionCreate.Returns<IActionResult>();

        var storageEntryTransactionCancel = modelBuilder.Action("StorageEntryTransactionsCancel");
        storageEntryTransactionCancel.Parameter<Guid>("Key");
        storageEntryTransactionCancel.Returns<IActionResult>();

        var storageTransactionsConfirmed = modelBuilder.Action("StorageTransactionsConfirmed");
        storageTransactionsConfirmed.Parameter<Guid>("Key");
        storageTransactionsConfirmed.Returns<IActionResult>();
        
        var storageTransactionsCancel = modelBuilder.Action("StorageTransactionsCancel");
        storageTransactionsCancel.Parameter<Guid>("Key");
        storageTransactionsCancel.Returns<IActionResult>();
        
         var storageTransactionsReverse = modelBuilder.Action("StorageTransactionsReverse");
         storageTransactionsReverse.Parameter<Guid>("Key");
         storageTransactionsReverse.Returns<IActionResult>();
        
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
        
        var salesContractsCancel = modelBuilder.Action("SalesContractsCancel");
        salesContractsCancel.Parameter<Guid>("Key");
        salesContractsCancel.Parameter<string>("Comments");
        salesContractsCancel.Returns<IActionResult>();
        
        var salesContractsAttachmentUpload = modelBuilder.Action("SalesContractsAttachmentUpload");
        salesContractsAttachmentUpload.Parameter<Guid>("ContractKey");
        salesContractsAttachmentUpload.Parameter<string>("Description");
        salesContractsAttachmentUpload.Parameter<string>("File");
        salesContractsAttachmentUpload.Parameter<string>("FileName");
        salesContractsAttachmentUpload.Parameter<string>("ContentType");
        salesContractsAttachmentUpload.Returns<IActionResult>();
        
        var salesContractsAttachmentDownload = modelBuilder.Function("SalesContractsAttachmentsDownload");
        salesContractsAttachmentDownload.Parameter<Guid>("Key");
        salesContractsAttachmentDownload.Returns<IActionResult>();
        
        var salesContractsAttachmentsListByContract = modelBuilder.Function("SalesContractsAttachmentsListByContract");
        salesContractsAttachmentsListByContract.Parameter<Guid>("ContractKey");
        salesContractsAttachmentsListByContract.Returns<ActionResult<ICollection<PurchaseContractAttachmentsDto>>>();
        
        var salesContractsGetTotals = modelBuilder.Function("SalesContractsGetTotals");
        salesContractsGetTotals.Parameter<Guid>("key");
        salesContractsGetTotals.Returns<SalesContractTotalsResponseDto>();
        
        var salesContractsGetAllocationsByContract = modelBuilder.Function("SalesContractsGetAllocationsByContract");
        salesContractsGetAllocationsByContract.Parameter<Guid>("SalesContractKey");
        salesContractsGetAllocationsByContract.Returns<ICollection<SalesContractsGetAllocationsByContractDto>>();
        
        var storageAddressesTotal = modelBuilder.Function("StorageAddressesTotals");
        storageAddressesTotal.Parameter<string>("code");
        storageAddressesTotal.Returns<IActionResult>();
        
        var storageAddressesListOpenedByItem = modelBuilder.Function("StorageAddressesListOpenedByItem");
        storageAddressesListOpenedByItem.Parameter<string>("Code");
        storageAddressesListOpenedByItem.Returns<IActionResult>();

        var storageAddressesReprocessing = modelBuilder.Action("StorageAddressesReprocessing");
        storageAddressesReprocessing.Parameter<string>("Code");
        storageAddressesReprocessing.Parameter<DateTime>("FromDate");
        storageAddressesReprocessing.Parameter<DateTime>("ToDate");
        storageAddressesReprocessing.Returns<IActionResult>();
        
        var storageAddressesDailyCalculationJob = modelBuilder.Action("StorageAddressesDailyCalculationJob");
        storageAddressesDailyCalculationJob.Parameter<string>("ProcessingDate");
        storageAddressesDailyCalculationJob.Returns<IActionResult>();
        
        var purchaseContractsGetAllocationsByContract = modelBuilder.Function("PurchaseContractsGetAllocationsByContract");
        purchaseContractsGetAllocationsByContract.Parameter<Guid>("PurchaseContractKey");
        purchaseContractsGetAllocationsByContract.Returns<ICollection<PurchaseContractAllocationsByContractDto>>();
        
        var purchaseContractsGetAvaiablesList = modelBuilder.Function("PurchaseContractsGetAvaiablesList");
        purchaseContractsGetAvaiablesList.Parameter<string>("CardCode");
        purchaseContractsGetAvaiablesList.Parameter<string>("ItemCode");
        purchaseContractsGetAvaiablesList.Returns<ICollection<PurchaseContractDto>>();

        var purchaseContractsGetShipmentReleasesAvailable = modelBuilder.Function("PurchaseContractsGetShipmentReleasesAvailable");
        purchaseContractsGetShipmentReleasesAvailable.Returns<PurchaseContract>();
        
        var notificationOutboxResend = modelBuilder.Action("NotificationOutboxResend");
        notificationOutboxResend.Parameter<Guid>("Key");
        notificationOutboxResend.Returns<IActionResult>();

        var purchaseContractsApproval = modelBuilder.Action("PurchaseContractsApproval");
        purchaseContractsApproval.Parameter<Guid>("Key");
        purchaseContractsApproval.Parameter<string>("Comments");
        purchaseContractsApproval.Returns<IActionResult>();
        
        var purchaseContractsReject = modelBuilder.Action("PurchaseContractsReject");
        purchaseContractsReject.Parameter<Guid>("Key");
        purchaseContractsReject.Parameter<string>("Comments");
        purchaseContractsReject.Returns<IActionResult>();
        
        var purchaseContractsCancel = modelBuilder.Action("PurchaseContractsCancel");
        purchaseContractsCancel.Parameter<Guid>("Key");
        purchaseContractsCancel.Parameter<string>("Comments");
        purchaseContractsCancel.Returns<IActionResult>();
        
        var purchaseContractsCreateAllocation = modelBuilder.Action("PurchaseContractsCreateAllocation");
        purchaseContractsCreateAllocation.Parameter<Guid>("PurchaseContractKey");
        purchaseContractsCreateAllocation.Parameter<Guid>("StorageTransactionKey");
        purchaseContractsCreateAllocation.Parameter<decimal>("Volume");
        purchaseContractsCreateAllocation.Returns<IActionResult>();
        
        var purchaseContractsDeleteAllocation = modelBuilder.Action("PurchaseContractsDeleteAllocation");
        purchaseContractsDeleteAllocation.Parameter<Guid>("Key");
        purchaseContractsDeleteAllocation.Returns<IActionResult>();

        var purchaseContractsRecalculateBalance = modelBuilder.Action("PurchaseContractsRecalculateBalance");
        purchaseContractsRecalculateBalance.Parameter<Guid>("Key");
        purchaseContractsRecalculateBalance.Returns<PurchaseContractRecalcResultDto>();

        var purchaseContractsRecalculateAllBalances = modelBuilder.Action("PurchaseContractsRecalculateAllBalances");
        purchaseContractsRecalculateAllBalances.Returns<PurchaseContractRecalcAllResultDto>();

        var purchaseContractsClose = modelBuilder.Action("PurchaseContractsClose");
        purchaseContractsClose.Parameter<Guid>("Key");
        purchaseContractsClose.Returns<IActionResult>();

        // Criação de fixação como action (e não POST na navegação): o frontend invoca
        // pelo ODataModel e a linha nova aparece sem recarregar a rota.
        var priceFixationCreate = modelBuilder.Action("PurchaseContractsPriceFixationCreate");
        priceFixationCreate.Parameter<Guid>("PurchaseContractKey");
        priceFixationCreate.EntityParameter<PurchaseContractPriceFixation>("Fixation");
        priceFixationCreate.Returns<IActionResult>();

        // Exclusão de fixação em aprovação como action. Key é a chave da FIXAÇÃO.
        var priceFixationDelete = modelBuilder.Action("PurchaseContractsPriceFixationDelete");
        priceFixationDelete.Parameter<Guid>("Key");
        priceFixationDelete.Returns<IActionResult>();

        // Fixação de preço: aprovação/rejeição pela diretoria e estorno de confirmada.
        // O parâmetro Key é a chave da FIXAÇÃO, não a do contrato.
        var priceFixationApproval = modelBuilder.Action("PurchaseContractsPriceFixationApproval");
        priceFixationApproval.Parameter<Guid>("Key");
        priceFixationApproval.Parameter<string>("Comments");
        priceFixationApproval.Returns<IActionResult>();

        var priceFixationReject = modelBuilder.Action("PurchaseContractsPriceFixationReject");
        priceFixationReject.Parameter<Guid>("Key");
        priceFixationReject.Parameter<string>("Comments");
        priceFixationReject.Returns<IActionResult>();

        var priceFixationCancel = modelBuilder.Action("PurchaseContractsPriceFixationCancel");
        priceFixationCancel.Parameter<Guid>("Key");
        priceFixationCancel.Returns<IActionResult>();

        var purchaseContractsReopen = modelBuilder.Action("PurchaseContractsReopen");
        purchaseContractsReopen.Parameter<Guid>("Key");
        purchaseContractsReopen.Returns<IActionResult>();
            
        var purchaseContractsAttachmentUpload = modelBuilder.Action("PurchaseContractsAttachmentUpload");
        purchaseContractsAttachmentUpload.Parameter<Guid>("ContractKey");
        purchaseContractsAttachmentUpload.Parameter<string>("Description");
        purchaseContractsAttachmentUpload.Parameter<string>("File");
        purchaseContractsAttachmentUpload.Parameter<string>("FileName");
        purchaseContractsAttachmentUpload.Parameter<string>("ContentType");
        purchaseContractsAttachmentUpload.Returns<IActionResult>();
        
        var purchaseContractsAttachmentDownload = modelBuilder.Function("PurchaseContractsAttachmentsDownload");
        purchaseContractsAttachmentDownload.Parameter<Guid>("Key");
        purchaseContractsAttachmentDownload.Returns<IActionResult>();
        
        var purchaseContractsAttachmentsListByContract = modelBuilder.Function("PurchaseContractsAttachmentsListByContract");
        purchaseContractsAttachmentsListByContract.Parameter<Guid>("ContractKey");
        purchaseContractsAttachmentsListByContract.Returns<ActionResult<ICollection<PurchaseContractAttachmentsDto>>>();
        
        var shipmentReleasesApprovation = modelBuilder.Action("ShipmentReleasesApprovation");
        shipmentReleasesApprovation.Parameter<Guid>("Key");
        shipmentReleasesApprovation.Returns<IActionResult>();
        
        var shipmentReleasesCancelation = modelBuilder.Action("ShipmentReleasesCancelation");
        shipmentReleasesCancelation.Parameter<Guid>("Key");
        shipmentReleasesCancelation.Parameter<string>("CancellationReason");
        shipmentReleasesCancelation.Returns<IActionResult>();
        
        var shipmentReleasesPause = modelBuilder.Action("ShipmentReleasesPause");
        shipmentReleasesPause.Parameter<Guid>("Key");
        shipmentReleasesPause.Returns<IActionResult>();
        
        var shipmentReleasesBalance = modelBuilder.Function("ShipmentReleasesGetBalance");
        shipmentReleasesBalance.Parameter<string>("ItemCode");
        shipmentReleasesBalance.Returns<ICollection<ShipmentRelesesBalanceResponseDto>>();
        
        var shipmentReleasesPurchaseContracts = modelBuilder.Function("ShipmentReleasesGetPurchaseContracts");
        shipmentReleasesPurchaseContracts.Parameter<string>("ItemCode");
        shipmentReleasesPurchaseContracts.Parameter<string>("WarehouseCode");
        shipmentReleasesPurchaseContracts.Returns<ICollection<ShipmentRelesesPurchaseContractsResponseDto>>();

        var shipmentReleasesRecalculateBalance = modelBuilder.Action("ShipmentReleasesRecalculateBalance");
        shipmentReleasesRecalculateBalance.Parameter<Guid>("Key");
        shipmentReleasesRecalculateBalance.Returns<ShipmentReleaseRecalcResultDto>();

        var shipmentReleasesRecalculateAllBalances = modelBuilder.Action("ShipmentReleasesRecalculateAllBalances");
        shipmentReleasesRecalculateAllBalances.Returns<ShipmentReleaseRecalcAllResultDto>();

        var shipmentReleasesClose = modelBuilder.Action("ShipmentReleasesClose");
        shipmentReleasesClose.Parameter<Guid>("Key");
        shipmentReleasesClose.Returns<IActionResult>();

        var shipmentReleasesReopen = modelBuilder.Action("ShipmentReleasesReopen");
        shipmentReleasesReopen.Parameter<Guid>("Key");
        shipmentReleasesReopen.Returns<IActionResult>();

        // ===== Liberações de entrega de VENDA (SalesShipmentRelease) =====
        var salesShipmentReleasesApprovation = modelBuilder.Action("SalesShipmentReleasesApprovation");
        salesShipmentReleasesApprovation.Parameter<Guid>("Key");
        salesShipmentReleasesApprovation.Returns<IActionResult>();

        var salesShipmentReleasesCancelation = modelBuilder.Action("SalesShipmentReleasesCancelation");
        salesShipmentReleasesCancelation.Parameter<Guid>("Key");
        salesShipmentReleasesCancelation.Parameter<string>("CancellationReason");
        salesShipmentReleasesCancelation.Returns<IActionResult>();

        var salesShipmentReleasesPause = modelBuilder.Action("SalesShipmentReleasesPause");
        salesShipmentReleasesPause.Parameter<Guid>("Key");
        salesShipmentReleasesPause.Returns<IActionResult>();

        var salesShipmentReleasesRecalculateBalance = modelBuilder.Action("SalesShipmentReleasesRecalculateBalance");
        salesShipmentReleasesRecalculateBalance.Parameter<Guid>("Key");
        salesShipmentReleasesRecalculateBalance.Returns<SalesShipmentReleaseRecalcResultDto>();

        var salesShipmentReleasesRecalculateAllBalances = modelBuilder.Action("SalesShipmentReleasesRecalculateAllBalances");
        salesShipmentReleasesRecalculateAllBalances.Returns<SalesShipmentReleaseRecalcAllResultDto>();

        var salesShipmentReleasesBackfillDeliveryLocationName = modelBuilder.Action("SalesShipmentReleasesBackfillDeliveryLocationName");
        salesShipmentReleasesBackfillDeliveryLocationName.Returns<SalesShipmentReleaseBackfillResultDto>();

        var salesShipmentReleasesClose = modelBuilder.Action("SalesShipmentReleasesClose");
        salesShipmentReleasesClose.Parameter<Guid>("Key");
        salesShipmentReleasesClose.Returns<IActionResult>();

        var salesShipmentReleasesReopen = modelBuilder.Action("SalesShipmentReleasesReopen");
        salesShipmentReleasesReopen.Parameter<Guid>("Key");
        salesShipmentReleasesReopen.Returns<IActionResult>();

        var salesShipmentReleasesGetAvailable = modelBuilder.Function("SalesShipmentReleasesGetAvailable");
        salesShipmentReleasesGetAvailable.Parameter<string>("ItemCode");
        salesShipmentReleasesGetAvailable.Returns<ICollection<SalesShipmentReleaseAvailableDto>>();

        var salesContractsClose = modelBuilder.Action("SalesContractsClose");
        salesContractsClose.Parameter<Guid>("Key");
        salesContractsClose.Returns<IActionResult>();

        var salesContractsReopen = modelBuilder.Action("SalesContractsReopen");
        salesContractsReopen.Parameter<Guid>("Key");
        salesContractsReopen.Returns<IActionResult>();

        var salesContractsGetShipmentReleasesAvailable = modelBuilder.Function("SalesContractsGetShipmentReleasesAvailable");
        salesContractsGetShipmentReleasesAvailable.Returns<SalesContract>();

        var salesContractsCreateReallocation = modelBuilder.Action("SalesContractsCreateReallocation");
        salesContractsCreateReallocation.Parameter<Guid>("SalesInvoiceItemKey");
        salesContractsCreateReallocation.Parameter<Guid>("SourceSalesContractKey");
        salesContractsCreateReallocation.Parameter<Guid>("TargetSalesContractKey");
        // Nulo = o servidor escolhe a liberação do destino por FIFO (data de liberação).
        salesContractsCreateReallocation.Parameter<Guid?>("TargetSalesShipmentReleaseKey");
        salesContractsCreateReallocation.Parameter<decimal>("Volume");
        // Opt-in explícito para o saldo do destino ficar negativo; exige Reason.
        salesContractsCreateReallocation.Parameter<bool>("AllowNegativeBalance");
        salesContractsCreateReallocation.Parameter<string>("Reason");
        salesContractsCreateReallocation.Returns<IActionResult>();

        var salesContractsGetReconciliationTargets = modelBuilder.Function("SalesContractsGetReconciliationTargets");
        salesContractsGetReconciliationTargets.Parameter<Guid>("SalesInvoiceItemKey");
        salesContractsGetReconciliationTargets.Parameter<Guid>("SourceSalesContractKey");
        salesContractsGetReconciliationTargets.Returns<ICollection<SalesContractReconciliationTargetDto>>();

        var salesContractsGetNegativeBalances = modelBuilder.Function("SalesContractsGetNegativeBalances");
        salesContractsGetNegativeBalances.Returns<ICollection<SalesContractNegativeBalanceDto>>();

        var salesContractsDeleteReallocation = modelBuilder.Action("SalesContractsDeleteReallocation");
        salesContractsDeleteReallocation.Parameter<Guid>("Key");
        salesContractsDeleteReallocation.Returns<IActionResult>();

        var salesContractsRecalculateBalance = modelBuilder.Action("SalesContractsRecalculateBalance");
        salesContractsRecalculateBalance.Parameter<Guid>("Key");
        salesContractsRecalculateBalance.Returns<SalesContractRecalcResultDto>();

        var salesContractsRecalculateAllBalances = modelBuilder.Action("SalesContractsRecalculateAllBalances");
        salesContractsRecalculateAllBalances.Returns<SalesContractRecalcAllResultDto>();

        // Fixação de preço de contrato de venda: criação/exclusão como actions (o frontend
        // invoca pelo ODataModel e a linha aparece/some sem recarregar a rota) e o ciclo de
        // aprovação da diretoria. Espelha as actions de PurchaseContractsPriceFixation*.
        var salesPriceFixationCreate = modelBuilder.Action("SalesContractsPriceFixationCreate");
        salesPriceFixationCreate.Parameter<Guid>("SalesContractKey");
        salesPriceFixationCreate.EntityParameter<SalesContractPriceFixation>("Fixation");
        salesPriceFixationCreate.Returns<IActionResult>();

        // Key é a chave da FIXAÇÃO, não a do contrato.
        var salesPriceFixationDelete = modelBuilder.Action("SalesContractsPriceFixationDelete");
        salesPriceFixationDelete.Parameter<Guid>("Key");
        salesPriceFixationDelete.Returns<IActionResult>();

        var salesPriceFixationApproval = modelBuilder.Action("SalesContractsPriceFixationApproval");
        salesPriceFixationApproval.Parameter<Guid>("Key");
        salesPriceFixationApproval.Parameter<string>("Comments");
        salesPriceFixationApproval.Returns<IActionResult>();

        var salesPriceFixationReject = modelBuilder.Action("SalesContractsPriceFixationReject");
        salesPriceFixationReject.Parameter<Guid>("Key");
        salesPriceFixationReject.Parameter<string>("Comments");
        salesPriceFixationReject.Returns<IActionResult>();

        var salesPriceFixationCancel = modelBuilder.Action("SalesContractsPriceFixationCancel");
        salesPriceFixationCancel.Parameter<Guid>("Key");
        salesPriceFixationCancel.Returns<IActionResult>();

        // Comentários do contrato (compra e venda). Tudo por action, e não POST/PATCH na coleção
        // aninhada, por dois motivos: a mutação e a linha do log de alterações entram no mesmo
        // SaveChanges de um service único, e o Detail do frontend não tem o "Salvar" da tela — o
        // update group é diferido, e uma escrita pendente ali derrubaria o batch inteiro.
        var purchaseContractsCommentCreate = modelBuilder.Action("PurchaseContractsCommentCreate");
        purchaseContractsCommentCreate.Parameter<Guid>("ContractKey");
        purchaseContractsCommentCreate.Parameter<string>("Text");
        purchaseContractsCommentCreate.Returns<IActionResult>();

        // Key é a chave do COMENTÁRIO, não a do contrato.
        var purchaseContractsCommentUpdate = modelBuilder.Action("PurchaseContractsCommentUpdate");
        purchaseContractsCommentUpdate.Parameter<Guid>("Key");
        purchaseContractsCommentUpdate.Parameter<string>("Text");
        purchaseContractsCommentUpdate.Returns<IActionResult>();

        var purchaseContractsCommentDelete = modelBuilder.Action("PurchaseContractsCommentDelete");
        purchaseContractsCommentDelete.Parameter<Guid>("Key");
        purchaseContractsCommentDelete.Returns<IActionResult>();

        var salesContractsCommentCreate = modelBuilder.Action("SalesContractsCommentCreate");
        salesContractsCommentCreate.Parameter<Guid>("ContractKey");
        salesContractsCommentCreate.Parameter<string>("Text");
        salesContractsCommentCreate.Returns<IActionResult>();

        var salesContractsCommentUpdate = modelBuilder.Action("SalesContractsCommentUpdate");
        salesContractsCommentUpdate.Parameter<Guid>("Key");
        salesContractsCommentUpdate.Parameter<string>("Text");
        salesContractsCommentUpdate.Returns<IActionResult>();

        var salesContractsCommentDelete = modelBuilder.Action("SalesContractsCommentDelete");
        salesContractsCommentDelete.Parameter<Guid>("Key");
        salesContractsCommentDelete.Returns<IActionResult>();

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
        
        var salesInvoicesReverseConfirm = modelBuilder.Action("SalesInvoicesReverseConfirm");
        salesInvoicesReverseConfirm.Parameter<Guid>("Key");
        salesInvoicesReverseConfirm.Returns<IActionResult>();
        
        var salesInvoicesSetDocumentNumber = modelBuilder.Action("SalesInvoicesSetDocumentNumber");
        salesInvoicesSetDocumentNumber.Parameter<Guid>("Key");
        salesInvoicesSetDocumentNumber.Parameter<string>("DocumentNumber");
        salesInvoicesSetDocumentNumber.Parameter<string>("DocumentSeries");
        salesInvoicesSetDocumentNumber.Parameter<string>("ChaveNFe");
        salesInvoicesSetDocumentNumber.Returns<IActionResult>();

        var salesInvoicesCommentCreate = modelBuilder.Action("SalesInvoicesCommentCreate");
        salesInvoicesCommentCreate.Parameter<Guid>("InvoiceKey");
        salesInvoicesCommentCreate.Parameter<string>("Text");
        salesInvoicesCommentCreate.Returns<IActionResult>();

        var salesInvoicesCommentUpdate = modelBuilder.Action("SalesInvoicesCommentUpdate");
        salesInvoicesCommentUpdate.Parameter<Guid>("Key");
        salesInvoicesCommentUpdate.Parameter<string>("Text");
        salesInvoicesCommentUpdate.Returns<IActionResult>();

        var salesInvoicesCommentDelete = modelBuilder.Action("SalesInvoicesCommentDelete");
        salesInvoicesCommentDelete.Parameter<Guid>("Key");
        salesInvoicesCommentDelete.Returns<IActionResult>();


        var weighingTicketsFirstWeighing = modelBuilder.Action("WeighingTicketsFirstWeighing");
        weighingTicketsFirstWeighing.Parameter<Guid>("Key");
        weighingTicketsFirstWeighing.Parameter<int>("Value");
        weighingTicketsFirstWeighing.Parameter<string>("Comments");
        weighingTicketsFirstWeighing.Returns<IActionResult>();
        
        var weighingTicketsSecondWeighing = modelBuilder.Action("WeighingTicketsSecondWeighing");
        weighingTicketsSecondWeighing.Parameter<Guid>("Key");
        weighingTicketsSecondWeighing.Parameter<int>("Value");
        weighingTicketsSecondWeighing.Parameter<string>("Comments");
        weighingTicketsSecondWeighing.Returns<IActionResult>();

        var weighingTicketsCompleted = modelBuilder.Action("WeighingTicketsCompleted");
        weighingTicketsCompleted.Parameter<Guid>("Key");
        weighingTicketsCompleted.Returns<IActionResult>();
        
        var weighingTicketsCancel = modelBuilder.Action("WeighingTicketsCancel");
        weighingTicketsCancel.Parameter<Guid>("Key");
        weighingTicketsCancel.Returns<IActionResult>();
        
        var weighingTicketsReOpen = modelBuilder.Action("WeighingTicketsReOpen");
        weighingTicketsReOpen.Parameter<Guid>("Key");
        weighingTicketsReOpen.Returns<IActionResult>();
        
        var ownershipTransfersConfirm = modelBuilder.Action("OwnershipTransfersConfirm");
        ownershipTransfersConfirm.Parameter<Guid>("Key");
        ownershipTransfersConfirm.Returns<IActionResult>();

        var ownershipTransfersCancel = modelBuilder.Action("OwnershipTransfersCancel");
        ownershipTransfersCancel.Parameter<Guid>("Key");   
        ownershipTransfersCancel.Returns<IActionResult>();
        
        var ownershipTransfersListStorageAddressesBalanceByProduct =
            modelBuilder.Function("OwnershipTransfersListStorageAddressesBalanceByProduct");
        ownershipTransfersListStorageAddressesBalanceByProduct.Parameter<string>("ItemCode");
        ownershipTransfersListStorageAddressesBalanceByProduct.Parameter<string>("IgnoreCode");
        ownershipTransfersListStorageAddressesBalanceByProduct.Returns<IActionResult>();

        var storageInvoiceClosing = modelBuilder.Action("StorageInvoiceClosing");
        storageInvoiceClosing.Parameter<Guid>("DocNumberKey");
        storageInvoiceClosing.Parameter<string>("StorageAddressCode");
        storageInvoiceClosing.Parameter<DateTime>("PeriodStart");
        storageInvoiceClosing.Parameter<DateTime>("PeriodEnd");
        storageInvoiceClosing.Parameter<DateTime>("ClosingDate");
        storageInvoiceClosing.Parameter<string?>("Notes");
        storageInvoiceClosing.Parameter<bool>("IncludeUnpricedItems");
        storageInvoiceClosing.Returns<IActionResult>();
        
        var storageInvoiceCancellation = modelBuilder.Action("StorageInvoiceCancellation");
        storageInvoiceCancellation.Parameter<Guid>("StorageInvoiceKey");
        storageInvoiceCancellation.Parameter<string>("Reason");
        storageInvoiceCancellation.Returns<IActionResult>();
        
        var storageInvoiceClose = modelBuilder.Action("StorageInvoiceClose");
        storageInvoiceClose.Parameter<Guid>("Key");
        storageInvoiceClose.Returns<IActionResult>();
        
        var storageInvoiceOpen = modelBuilder.Action("StorageInvoiceOpen");
        storageInvoiceOpen.Parameter<Guid>("Key");
        storageInvoiceOpen.Returns<IActionResult>();
        
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