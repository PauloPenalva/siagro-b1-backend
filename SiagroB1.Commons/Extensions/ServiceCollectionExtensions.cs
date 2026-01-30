using Microsoft.Extensions.DependencyInjection;

using SiagroB1.Application.Companies;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.OwnershipTransfers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Application.SalesContracts;
using SiagroB1.Application.SalesInvoices;
using SiagroB1.Application.Services;
using SiagroB1.Application.ShipmentBilling;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Application.ShippingOrders;
using SiagroB1.Application.ShippingTransactions;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Application.WeighingTickets;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.SAP;

namespace SiagroB1.Commons.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSapServices(this IServiceCollection services)
    {
        services.AddScoped<IBusinessPartnerService, Application.Services.SAP.BusinessPartnerService>();
        services.AddScoped<IItemService, Application.Services.SAP.ItemService>();
        services.AddScoped<IUnitOfMeasureService, Application.Services.SAP.UnitOfMeasureService>();
        services.AddScoped<IAgentService, Application.Services.SAP.AgentService>();
        services.AddScoped<IWarehouseService, Application.Services.SAP.WarehouseService>();
        
        return services;
    }
    
    public static IServiceCollection AddStandAloneServices(this IServiceCollection services)
    {
        //services.AddScoped<IBusinessPartnerService, Application.Services.BusinessPartnerService>();
        //services.AddScoped<IItemService, Application.Services.ItemService>();
        services.AddScoped<IUnitOfMeasureService, UnitOfMeasureService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        
        return services;
    }
    
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // commons services ( services folder )
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<DocNumberService>();
        services.AddScoped<IHarvestSeasonService, HarvestSeasonService>();
        services.AddScoped<LogisticRegionService>();
        services.AddScoped<IProcessingCostDryingDetailService, ProcessingCostDryingDetailService>();
        services.AddScoped<IProcessingCostDryingParameterService, ProcessingCostDryingParameterService>();
        services.AddScoped<IProcessingCostQualityParameterService, ProcessingCostQualityParameterService>();
        services.AddScoped<IProcessingCostService, ProcessingCostService>();
        services.AddScoped<IProcessingServiceService, ProcessingServiceService>();
        services.AddScoped<IPurchaseContractService, PurchaseContractService>();
        services.AddScoped<IStateService, StateService>();
        services.AddScoped<ITaxService, TaxService>();
        services.AddScoped<ITruckDriverService, TruckDriverService>();
        services.AddScoped<ITruckService, TruckService>();
        services.AddScoped<IWeighingTicketService, WeighingTicketService>();
        services.AddScoped<IQualityAttribService, QualityAttribService>();
        
        // companies
        services.AddScoped<CompaniesCreateService>();
        services.AddScoped<CompaniesDeleteService>();
        services.AddScoped<CompaniesGetService>();
        services.AddScoped<CompaniesUpdateService>();

        // doc numbers
        services.AddScoped<DocNumberSequenceService>();
        
        // purchase contracts
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
        services.AddScoped<PurchaseContractsPriceFixationsCreateService>();
        services.AddScoped<PurchaseContractsPriceFixationsDeleteService>();
        services.AddScoped<PurchaseContractsPriceFixationsGetService>();
        services.AddScoped<PurchaseContractsPriceFixationsUpdateService>();
        services.AddScoped<PurchaseContractsQualityParametersCreateService>();
        services.AddScoped<PurchaseContractsQualityParametersDeleteService>();
        services.AddScoped<PurchaseContractsQualityParametersGetService>();
        services.AddScoped<PurchaseContractsQualityParametersUpdateService>();
        services.AddScoped<PurchaseContractsRejectService>();
        services.AddScoped<PurchaseContractsSendApprovalService>();
        services.AddScoped<PurchaseContractsTaxesCreateService>();
        services.AddScoped<PurchaseContractsTaxesDeleteService>();
        services.AddScoped<PurchaseContractsTaxesGetService>();
        services.AddScoped<PurchaseContractsTaxesUpdateService>();
        services.AddScoped<PurchaseContractsTotalsService>();
        services.AddScoped<PurchaseContractsUpdateService>();
        services.AddScoped<PurchaseContractsWithdrawApprovalService>();
        services.AddScoped<PurchaseContractsGetAllocationsByContractService>();
        
        // sales contracts
        services.AddScoped<SalesContractsApprovalService>();
        services.AddScoped<SalesContractsCopyService>();
        services.AddScoped<SalesContractsCreateService>();
        services.AddScoped<SalesContractsDeleteService>();
        services.AddScoped<SalesContractsGetService>();
        services.AddScoped<SalesContractsRejectService>();
        services.AddScoped<SalesContractsSendApprovalService>();
        services.AddScoped<SalesContractsTotalsService>();
        services.AddScoped<SalesContractsUpdateService>();
        services.AddScoped<SalesContractsWithdrawApprovalService>();

        // sales invoices
        services.AddScoped<SalesInvoicesCancelService>();
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
        
        // shipment billing
        services.AddScoped<ShipmentBillingCreateSalesInvoiceService>();
        services.AddScoped<ShipmentBillingDeleteService>();
        
        // shipment releases
        services.AddScoped<ShipmentReleasesApprovationService>();
        services.AddScoped<ShipmentReleasesBalanceService>();
        services.AddScoped<ShipmentReleasesCancelationService>();
        services.AddScoped<ShipmentReleasesCreateService>();
        services.AddScoped<ShipmentReleasesDeleteService>();
        services.AddScoped<ShipmentReleasesGetService>();
        services.AddScoped<ShipmentReleasesUpdateService>();

        // shipping orders
        services.AddScoped<ShippingOrdersCancelService>();
        services.AddScoped<ShippingOrdersCreateService>();
        services.AddScoped<ShippingOrdersDeleteService>();
        services.AddScoped<ShippingOrdersGetService>();
        services.AddScoped<ShippingOrdersShippedService>();
        services.AddScoped<ShippingOrdersUpdateService>();
         
        // shipping transactions
        services.AddScoped<ShippingTransactionsCreateService>();
        
        // storage addresses
        services.AddScoped<StorageAddressesCreateService>();
        services.AddScoped<StorageAddressesDeleteService>();
        services.AddScoped<StorageAddressesGetService>();
        services.AddScoped<StorageAddressesTotalsService>();
        services.AddScoped<StorageAddressesUpdateService>();
        services.AddScoped<StorageAddressesListOpenedByItemService>();

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
        
        // weighing tickets
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
        
        //ownership transfers
        services.AddScoped<OwnershipTransfersCreateService>();
        services.AddScoped<OwnershipTransfersUpdateService>();
        services.AddScoped<OwnershipTransfersGetService>();
        services.AddScoped<OwnershipTransfersConfirmService>();
        
        return services;
    }
}