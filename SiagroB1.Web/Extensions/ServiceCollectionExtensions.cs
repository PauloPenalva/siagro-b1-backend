using SiagroB1.Application.Jobs;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.MenuItem;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Application.Services.OwnershipTransfers;
using SiagroB1.Application.Services.Permissions;
using SiagroB1.Application.Services.ProcessingCosts;
using SiagroB1.Application.Services.Profiles;
using SiagroB1.Application.Services.ProfilesRoles;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Roles;
using SiagroB1.Application.Services.RolesMenus;
using SiagroB1.Application.Services.RolesPermissions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShippingOrders;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageEntryTransactions;
using SiagroB1.Application.Services.StorageInvoices;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.Security;
using SiagroB1.Security.Shared;
using SiagroB1.Application.Services.Users;
using SiagroB1.Application.Services.UserTruckScales;
using SiagroB1.Application.Services.UsersProfiles;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Interfaces;
using SiagroB1.Commons.Scales;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Web.Sockets;
using SiagroB1.Web.Sockets.TruckScale;
using BranchService = SiagroB1.Application.Services.BranchService;

namespace SiagroB1.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSapServices(this IServiceCollection services)
    {
        services.AddScoped<IBusinessPartnerService, Application.Services.SAP.BusinessPartnerService>();
        services.AddScoped<IItemService, Application.Services.SAP.ItemService>();
        services.AddScoped<IUnitOfMeasureService, Application.Services.SAP.UnitOfMeasureService>();
        services.AddScoped<IAgentService, Application.Services.SAP.AgentService>();
        services.AddScoped<IWarehouseService, Application.Services.SAP.WarehouseService>();
        services.AddScoped<ICostCenterService, Application.Services.SAP.CostCenterService>();
        services.AddScoped<ILedgerAccountService, Application.Services.SAP.LedgerAccountService>();
        services.AddScoped<IUsage, Application.Services.SAP.UsageService>();

        return services;
    }
    
    public static IServiceCollection AddStandAloneServices(this IServiceCollection services)
    {
        services.AddScoped<IBusinessPartnerService, Application.Services.BusinessPartnerService>();
        services.AddScoped<IItemService, Application.Services.ItemService>();
        services.AddScoped<IUnitOfMeasureService, Application.Services.UnitOfMeasureService>();
        services.AddScoped<IAgentService, Application.Services.AgentService>();
        services.AddScoped<IWarehouseService, Application.Services.WarehouseService>();
        services.AddScoped<IBusinessPartnerAddressService, Application.Services.BusinessPartnerAddressService>();
        services.AddScoped<ICostCenterService, Application.Services.CostCenterService>();
        services.AddScoped<ILedgerAccountService, Application.Services.LedgerAccountService>();
        services.AddScoped<IUsage, Application.Services.UsageService>();

        return services;
    }
    
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // websocket da balança rodoviária
        // Janela de 3 s com no mínimo 5 amostras; sem leitura por 2 s a balança é dada como offline.
        services.AddSingleton(new LiveReadingStore(
            window: TimeSpan.FromSeconds(3),
            minimumSamples: 5,
            offlineAfter: TimeSpan.FromSeconds(2)));

        services.AddSingleton<TruckScaleHub>();
        services.AddScoped<ScaleConfigProvider>();

        // commons services ( services folder )
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IHarvestSeasonService, HarvestSeasonService>();
        services.AddScoped<LogisticRegionService>();
        services.AddScoped<IProcessingServiceService, ProcessingServiceService>();
        services.AddScoped<IPurchaseContractService, PurchaseContractService>();
        services.AddScoped<IStateService, StateService>();
        services.AddScoped<ITaxService, TaxService>();
        services.AddScoped<ITruckDriverService, TruckDriverService>();
        services.AddScoped<ITruckService, TruckService>();
        services.AddScoped<IWeighingTicketService, WeighingTicketService>();
        services.AddScoped<IQualityAttribService, QualityAttribService>();
        services.AddScoped<TruckScaleService>();
        
        // environment setup
        services.AddScoped<SystemSetupService>();
        
        // users
        // A política de senha é do Security e já era registrada no Gateway (reset por e-mail e
        // perfil); o Web precisa dela porque a criação do usuário também valida a senha.
        services.AddSingleton<PasswordPolicy>();
        services.AddScoped<UsersCreateService>();
        services.AddScoped<UsersUpdateService>();
        services.AddScoped<UsersGetService>();
        services.AddScoped<UsersDeleteService>();
        
        // menu items
        services.AddScoped<MenuItemsCreateService>();
        services.AddScoped<MenuItemsUpdateService>();
        services.AddScoped<MenuItemsDeleteService>();
        services.AddScoped<MenuItemsGetService>();
        
        // permissions
        services.AddScoped<PermissionsCreateService>();
        services.AddScoped<PermissionsUpdateService>();
        services.AddScoped<PermissionsGetService>();
        services.AddScoped<PermissionsDeleteService>(); 
        
        // roles
        services.AddScoped<RolesCreateService>();
        services.AddScoped<RolesUpdateService>();
        services.AddScoped<RolesGetService>();
        services.AddScoped<RolesDeleteService>();
        
        // roles permissions
        services.AddScoped<RolesPermissionsCreateService>();
        services.AddScoped<RolesPermissionsUpdateService>();
        services.AddScoped<RolesPermissionsGetService>();
        services.AddScoped<RolesPermissionsDeleteService>();
        
        // roles menus
        services.AddScoped<RolesMenusCreateService>();
        services.AddScoped<RolesMenusUpdateService>();
        services.AddScoped<RolesMenusGetService>();
        services.AddScoped<RolesMenusDeleteService>();
        
        // profiles
        services.AddScoped<ProfilesCreateService>();
        services.AddScoped<ProfilesUpdateService>();
        services.AddScoped<ProfilesDeleteService>();
        services.AddScoped<ProfilesGetService>();
        
        // profiles roles
        services.AddScoped<ProfilesRolesCreateService>();
        services.AddScoped<ProfilesRolesUpdateService>();
        services.AddScoped<ProfilesRolesDeleteService>();
        services.AddScoped<ProfilesRolesGetService>();

        // users profiles
        services.AddScoped<UsersProfilesCreateService>();
        services.AddScoped<UsersProfilesUpdateService>();
        services.AddScoped<UsersProfilesDeleteService>();
        services.AddScoped<UsersProfilesGetService>();
        
        // companies
        services.AddScoped<CompaniesCreateService>();
        services.AddScoped<CompaniesDeleteService>();
        services.AddScoped<CompaniesGetService>();
        services.AddScoped<CompaniesUpdateService>();

        // doc numbers
        services.AddScoped<DocNumberSequenceService>();
        services.AddScoped<DocNumberGetInfoByTransactionCodeService>();
        services.AddScoped<DocNumberService>();
        
        // processing costs
        services.AddScoped<IProcessingCostDryingDetailService, ProcessingCostDryingDetailService>();
        services.AddScoped<IProcessingCostDryingParameterService, ProcessingCostDryingParameterService>();
        services.AddScoped<IProcessingCostQualityParameterService, ProcessingCostQualityParameterService>();
        services.AddScoped<IProcessingCostServiceDetailService, ProcessingCostServiceDetailService>();
        services.AddScoped<IProcessingCostService, ProcessingCostService>();
        services.AddScoped<IProcessingCostCsvImportService, ProcessingCostCsvImportService>();

        // notifications
        services.AddScoped<ContractNotificationOutboxService>();
        services.AddScoped<ContractNotificationPayloadBuilder>();
        services.AddScoped<NotificationRecipientResolver>();
        services.AddScoped<NotificationGroupsService>();
        services.AddScoped<NotificationGroupMembersService>();
        services.AddScoped<NotificationGroupSubscriptionsService>();
        services.AddScoped<NotificationOutboxGetService>();
        services.AddScoped<NotificationDeliveryLogsGetService>();
        services.AddScoped<NotificationOutboxResendService>();
        services.AddScoped<IContractNotificationDispatchJob, ContractNotificationDispatchJob>();
        services.AddScoped<IContractNotificationSweepJob, ContractNotificationSweepJob>();

        // purchase contracts
        services.AddScoped<PurchaseContractsRecalculateBalanceService>();
        services.AddScoped<PurchaseContractsCloseService>();
        services.AddScoped<PurchaseContractsReopenService>();
        services.AddScoped<PurchaseContractsAllocationCreateService>();
        services.AddScoped<PurchaseContractsAllocationDeleteService>();
        services.AddScoped<PurchaseContractsAllocationGetService>();
        services.AddScoped<PurchaseContractsApprovalService>();
        services.AddScoped<PurchaseContractsBrokersCreateService>();
        services.AddScoped<PurchaseContractsBrokersDeleteService>();
        services.AddScoped<PurchaseContractsBrokersGetService>();
        services.AddScoped<PurchaseContractsBrokersUpdateService>();
        services.AddScoped<PurchaseContractsCopyService>();
        services.AddScoped<PurchaseContractsCreateService>();
        services.AddScoped<PurchaseContractsDeleteService>();
        services.AddScoped<PurchaseContractsGetService>();
        services.AddScoped<PurchaseContractsGetShipmentReleasesAvailableService>();
        services.AddScoped<PurchaseContractsFixedVolumeService>();
        services.AddScoped<PurchaseContractsPriceFixationsApprovalService>();
        services.AddScoped<PurchaseContractsPriceFixationsCancelService>();
        services.AddScoped<PurchaseContractsPriceFixationCreateService>();
        services.AddScoped<PurchaseContractsPriceFixationsRejectService>();
        services.AddScoped<PurchaseContractsPriceFixationDeleteService>();
        services.AddScoped<PurchaseContractsPriceFixationsGetService>();
        services.AddScoped<PurchaseContractsPriceFixationsUpdateService>();
        services.AddScoped<PurchaseContractsQualityParametersCreateService>();
        services.AddScoped<PurchaseContractsQualityParametersDeleteService>();
        services.AddScoped<PurchaseContractsQualityParametersGetService>();
        services.AddScoped<PurchaseContractsQualityParametersUpdateService>();
        services.AddScoped<PurchaseContractsRejectService>();
        services.AddScoped<PurchaseContractsCancelService>();
        services.AddScoped<PurchaseContractsSendApprovalService>();
        services.AddScoped<PurchaseContractsTaxesCreateService>();
        services.AddScoped<PurchaseContractsTaxesDeleteService>();
        services.AddScoped<PurchaseContractsTaxesGetService>();
        services.AddScoped<PurchaseContractsTaxesUpdateService>();
        services.AddScoped<PurchaseContractsTotalsService>();
        services.AddScoped<PurchaseContractsUpdateService>();
        services.AddScoped<PurchaseContractsWithdrawApprovalService>();
        services.AddScoped<PurchaseContractsGetAllocationsByContractService>();
        services.AddScoped<PurchaseContractsChangeLogService>();
        services.AddScoped<PurchaseContractsChangeLogsGetService>();
        services.AddScoped<PurchaseContractsAttachmentsCreateService>();
        services.AddScoped<PurchaseContractsAttachmentsDeleteService>();
        services.AddScoped<PurchaseContractsAttachmentsGetService>();
        services.AddScoped<PurchaseContractsCommentCreateService>();
        services.AddScoped<PurchaseContractsCommentUpdateService>();
        services.AddScoped<PurchaseContractsCommentDeleteService>();
        services.AddScoped<PurchaseContractsCommentsGetService>();

        // sales contracts
        services.AddScoped<SalesContractsApprovalService>();
        services.AddScoped<SalesContractsCopyService>();
        services.AddScoped<SalesContractsCreateService>();
        services.AddScoped<SalesContractsDeleteService>();
        services.AddScoped<SalesContractsGetService>();
        services.AddScoped<SalesContractsRejectService>();
        services.AddScoped<SalesContractsCancelService>();
        services.AddScoped<SalesContractsSendApprovalService>();
        services.AddScoped<SalesContractsTotalsService>();
        services.AddScoped<SalesContractsUpdateService>();
        services.AddScoped<SalesContractsWithdrawApprovalService>();
        services.AddScoped<SalesContractsGetAllocationsByContractService>();
        services.AddScoped<SalesContractsAttachmentsCreateService>();
        services.AddScoped<SalesContractsAttachmentsDeleteService>();
        services.AddScoped<SalesContractsAttachmentsGetService>();
        services.AddScoped<SalesContractsCloseService>();
        services.AddScoped<SalesContractsReopenService>();
        services.AddScoped<SalesContractsGetShipmentReleasesAvailableService>();
        services.AddScoped<SalesContractsRecalculateBalanceService>();
        services.AddScoped<SalesContractsAllocationGetService>();
        services.AddScoped<SalesContractsAllocationCreateService>();
        services.AddScoped<SalesContractsAllocationCreateForReturnService>();
        services.AddScoped<SalesContractsAllocationDeleteForInvoiceService>();
        services.AddScoped<SalesContractsReallocationCreateService>();
        services.AddScoped<SalesContractsReallocationDeleteService>();
        services.AddScoped<SalesContractsGetReconciliationTargetsService>();
        services.AddScoped<SalesContractsGetNegativeBalancesService>();
        services.AddScoped<SalesContractsFixedVolumeService>();
        services.AddScoped<SalesContractsPriceFixationCreateService>();
        services.AddScoped<SalesContractsPriceFixationsApprovalService>();
        services.AddScoped<SalesContractsPriceFixationsRejectService>();
        services.AddScoped<SalesContractsPriceFixationsCancelService>();
        services.AddScoped<SalesContractsPriceFixationsUpdateService>();
        services.AddScoped<SalesContractsPriceFixationDeleteService>();
        services.AddScoped<SalesContractsPriceFixationsGetService>();
        services.AddScoped<SalesContractsChangeLogService>();
        services.AddScoped<SalesContractsChangeLogsGetService>();
        services.AddScoped<SalesContractsDeliveryLocationsCreateService>();
        services.AddScoped<SalesContractsDeliveryLocationsUpdateService>();
        services.AddScoped<SalesContractsDeliveryLocationsDeleteService>();
        services.AddScoped<SalesContractsDeliveryLocationsGetService>();
        services.AddScoped<SalesContractsCommentCreateService>();
        services.AddScoped<SalesContractsCommentUpdateService>();
        services.AddScoped<SalesContractsCommentDeleteService>();
        services.AddScoped<SalesContractsCommentsGetService>();

        // sales invoices
        services.AddScoped<SalesInvoicesCancelService>();
        services.AddScoped<SalesContractsAllocationCreateForFiscalAdjustmentService>();
        services.AddScoped<SalesInvoicesCfopResolveService>();
        services.AddScoped<SalesInvoicesUsageGuardService>();
        services.AddScoped<SalesInvoicesCreateService>();
        services.AddScoped<SalesInvoicesDeleteService>();
        services.AddScoped<SalesInvoicesGetService>();
        services.AddScoped<SalesInvoicesItemsCreateService>();
        services.AddScoped<SalesInvoicesItemsDeleteService>();
        services.AddScoped<SalesInvoicesItemsGetService>();
        services.AddScoped<SalesInvoicesItemsUpdateService>();
        services.AddScoped<SalesInvoicesUpdateService>();
        services.AddScoped<SalesInvoicesReturnService>();
        services.AddScoped<SalesInvoicesConfirmService>();
        services.AddScoped<SalesInvoicesSetDocumentNumberService>();
        services.AddScoped<SalesInvoicesReverseConfirmService>();
        services.AddScoped<SalesInvoicesChangeLogService>();
        services.AddScoped<SalesInvoicesChangeLogsGetService>();
        services.AddScoped<SalesInvoicesCommentCreateService>();
        services.AddScoped<SalesInvoicesCommentUpdateService>();
        services.AddScoped<SalesInvoicesCommentDeleteService>();
        services.AddScoped<SalesInvoicesCommentsGetService>();

        // documento de entrada (NF de terceiro e emissão própria)
        services.AddScoped<PurchaseInvoicesGetService>();
        services.AddScoped<PurchaseInvoicesGetOriginItemsService>();
        services.AddScoped<PurchaseInvoicesCreateService>();
        services.AddScoped<PurchaseInvoicesUpdateService>();
        services.AddScoped<PurchaseInvoicesDeleteService>();
        services.AddScoped<PurchaseInvoicesConfirmService>();
        services.AddScoped<PurchaseInvoicesReverseConfirmService>();
        services.AddScoped<PurchaseInvoicesCancelService>();
        services.AddScoped<PurchaseInvoicesImportXmlService>();
        services.AddScoped<PurchaseInvoicesItemsGetService>();
        services.AddScoped<PurchaseInvoicesItemsCreateService>();
        services.AddScoped<PurchaseInvoicesItemsUpdateService>();
        services.AddScoped<PurchaseInvoicesItemsDeleteService>();
        services.AddScoped<PurchaseInvoicesChangeLogService>();
        services.AddScoped<PurchaseInvoicesChangeLogsGetService>();
        services.AddScoped<PurchaseInvoicesCommentCreateService>();
        services.AddScoped<PurchaseInvoicesCommentUpdateService>();
        services.AddScoped<PurchaseInvoicesCommentDeleteService>();
        services.AddScoped<PurchaseInvoicesCommentsGetService>();

        // shipment billing
        services.AddScoped<ShipmentBillingCreateSalesInvoiceService>();
        services.AddScoped<ShipmentBillingTransactionGuardService>();
        services.AddScoped<ShippingTransactionsReverseService>();
        
        // shipment releases
        services.AddScoped<ShipmentReleasesRecalculateShippedService>();
        services.AddScoped<ShipmentReleasesRecalculateBalanceService>();
        services.AddScoped<ShipmentReleasesCloseService>();
        services.AddScoped<ShipmentReleasesReopenService>();
        services.AddScoped<ShipmentReleaseMovementGuardService>();
        services.AddScoped<ShipmentReleasesApprovationService>();
        services.AddScoped<ShipmentReleasesBalanceService>();
        services.AddScoped<ShipmentReleasesCancelationService>();
        services.AddScoped<ShipmentReleasesCreateService>();
        services.AddScoped<ShipmentReleasesDeleteService>();
        services.AddScoped<ShipmentReleasesGetService>();
        services.AddScoped<ShipmentReleasesUpdateService>();
        services.AddScoped<ShipmentReleasesPauseService>();
        services.AddScoped<ShipmentReleasesPurchaseContractsService>();

        // sales shipment releases
        services.AddScoped<SalesShipmentReleasesRecalculateShippedService>();
        services.AddScoped<SalesShipmentReleasesRecalculateBalanceService>();
        services.AddScoped<SalesShipmentReleasesCloseService>();
        services.AddScoped<SalesShipmentReleasesReopenService>();
        services.AddScoped<SalesShipmentReleaseMovementGuardService>();
        services.AddScoped<SalesShipmentReleasesApprovationService>();
        services.AddScoped<SalesShipmentReleasesCancelationService>();
        services.AddScoped<SalesShipmentReleasesCreateService>();
        services.AddScoped<SalesShipmentReleasesBackfillDeliveryLocationNameService>();
        services.AddScoped<SalesShipmentReleasesDeleteService>();
        services.AddScoped<SalesShipmentReleasesGetService>();
        services.AddScoped<SalesShipmentReleasesUpdateService>();
        services.AddScoped<SalesShipmentReleasesPauseService>();
        services.AddScoped<SalesShipmentReleasesGetAvailableService>();
        services.AddScoped<ShipmentLoadsCreateService>();
        services.AddScoped<ShipmentLoadsCancelService>();
        services.AddScoped<ShipmentLoadsGetService>();
        services.AddScoped<ShipmentLoadsMovementLogService>();
        services.AddScoped<ShipmentLoadsBillingGuardService>();
        services.AddScoped<ShipmentLoadsRecalculateInvoicedService>();
        services.AddScoped<ShipmentLoadsBalanceHookService>();

        // shipping orders
        services.AddScoped<ShippingOrdersCancelService>();
        services.AddScoped<ShippingOrdersCreateService>();
        services.AddScoped<ShippingOrdersDeleteService>();
        services.AddScoped<ShippingOrdersGetService>();
        services.AddScoped<ShippingOrdersShippedService>();
        services.AddScoped<ShippingOrdersUpdateService>();
         
        // shipping transactions
        services.AddScoped<ShippingTransactionsCreateService>();

        // storage entry transactions (entrada de compra em armazenagem própria)
        services.AddScoped<StorageEntryTransactionsCreateService>();
        services.AddScoped<StorageEntryTransactionsCancelService>();
        
        // storage addresses
        services.AddScoped<StorageAddressesCreateService>();
        services.AddScoped<StorageAddressesDeleteService>();
        services.AddScoped<StorageAddressesGetService>();
        services.AddScoped<StorageAddressesTotalsService>();
        services.AddScoped<StorageAddressesUpdateService>();
        services.AddScoped<StorageAddressesListOpenedByItemService>();
        services.AddScoped<StorageAddressesGetBalanceService>();
        services.AddScoped<IStorageAddressBalanceReader>(sp =>
            sp.GetRequiredService<StorageAddressesGetBalanceService>());
        services.AddScoped<StorageAddressesCalculationOrchestratorService>();
        services.AddScoped<StorageAddressesDailyBalanceBuilderService>();
        services.AddScoped<StorageAddressesStorageChargeCalculatorService>();
        services.AddScoped<StorageAddressesTechnicalLossCalculatorService>();
        services.AddScoped<StorageAddressesReprocessingService>();
        services.AddScoped<IStorageAddressesDailyCalculationJob, StorageAddressesDailyCalculationJob>();

        // storage transactions
        services.AddScoped<StorageTransactionsCancelService>();
        services.AddScoped<StorageTransactionsConfirmedService>();
        services.AddScoped<StorageTransactionsCopyService>();
        services.AddScoped<StorageTransactionsCreateService>();
        services.AddScoped<StorageTransactionsDeleteService>();
        services.AddScoped<StorageTransactionsGetService>();
        services.AddScoped<StorageTransactionsQualityInspectionsCreateService>();
        services.AddScoped<StorageTransactionsQualityInspectionsDeleteService>();
        services.AddScoped<StorageTransactionsQualityInspectionsGetService>();
        services.AddScoped<StorageTransactionsQualityInspectionsUpdateService>();
        services.AddScoped<StorageTransactionsUpdateService>();
        services.AddScoped<StorageTransactionsReverseService>();
        
        // weighing tickets
        services.AddScoped<IUserPermissions, UserPermissionsService>();
        services.AddScoped<WeighingCaptureValidator>();

        // balanças do usuário
        services.AddScoped<UserTruckScalesGetService>();
        services.AddScoped<UserTruckScalesCreateService>();
        services.AddScoped<UserTruckScalesUpdateService>();
        services.AddScoped<UserTruckScalesDeleteService>();

        // Singleton: o comprovante nasce num request (a captura) e é consumido em outro (a
        // pesagem). Escopo por request perderia o que acabou de emitir.
        services.AddSingleton(new CaptureStore(TimeSpan.FromMinutes(10)));

        services.AddScoped<WeighingTicketsCancelService>();
        services.AddScoped<WeighingTicketsCompletedService>();
        services.AddScoped<WeighingTicketsCreateService>();
        services.AddScoped<WeighingTicketsDeleteService>();
        services.AddScoped<WeighingTicketsFirstWeighingService>();
        services.AddScoped<WeighingTicketsGetService>();
        services.AddScoped<WeighingTicketsQualityInspectionsCreateService>();
        services.AddScoped<WeighingTicketsQualityInspectionsDeleteService>();
        services.AddScoped<WeighingTicketsQualityInspectionsGetService>();
        services.AddScoped<WeighingTicketsQualityInspectionsService>();
        services.AddScoped<WeighingTicketsQualityInspectionsUpdateService>();
        services.AddScoped<WeighingTicketsSecondWeighingService>();
        services.AddScoped<WeighingTicketsUpdateService>();
        services.AddScoped<WeighingTicketsReOpenService>();
        
        //ownership transfers
        services.AddScoped<OwnershipTransfersCreateService>();
        services.AddScoped<OwnershipTransfersUpdateService>();
        services.AddScoped<OwnershipTransfersGetService>();
        services.AddScoped<OwnershipTransfersConfirmService>();
        services.AddScoped<OwnershipTransfersCancelService>();
        services.AddScoped<OwnershipTransfersValidateContractService>();
        services.AddScoped<OwnershipTransfersListStorageAddressesBalanceByProductService>();

        //storage invoices
        services.AddScoped<IStorageInvoiceClosingService,StorageInvoiceClosingService>();
        services.AddScoped<IStorageInvoiceCancellationService,StorageInvoiceCancellationService>();
        services.AddScoped<IStorageInvoiceCloseService,StorageInvoiceCloseService>();
        services.AddScoped<IStorageInvoiceOpenService,StorageInvoiceOpenService>();
        services.AddScoped<StorageInvoicesGetService>();
        
        return services;
    }
}