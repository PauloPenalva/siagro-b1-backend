using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageInvoices;

public class StorageInvoiceClosingService(
    IUnitOfWork db,
    DocNumberSequenceService numberSequenceService,
    IStringLocalizer<Resource> resource,
    ILogger<StorageInvoiceClosingService> logger)
    : IStorageInvoiceClosingService
{
    public async Task<StorageInvoice> CloseAsync(
        StorageInvoiceCloseRequest request,
        string userName,
        CancellationToken ct = default)
    {
        if (request.DocNumberKey == Guid.Empty)
            throw new ApplicationException(resource["STORAGE_INVOICE_DOC_NUMBER_KEY_NOT_INFORMED"]);
        
        var periodStart = request.PeriodStart.Date;
        var periodEnd = request.PeriodEnd.Date;

        if (periodEnd < periodStart)
            throw new BusinessException(resource["INVALID_PERIOD"]);

        var address = await db.Context.StorageAddresses
            .FirstOrDefaultAsync(x => x.Code == request.StorageAddressCode, ct)
            ?? throw new NotFoundException(resource["STORAGE_ADDRESS_NOT_FOUND"]);

        var alreadyExists =await db.Context.StorageInvoices
            .AnyAsync(x =>
                x.StorageAddressCode == request.StorageAddressCode &&
                x.Status != StorageInvoiceStatus.Cancelled &&
                x.PeriodStart <= periodEnd &&
                x.PeriodEnd >= periodStart, ct);

        if (alreadyExists)
            throw new BusinessException(resource["STORAGE_INVOICE_ALREADY_EXISTS"]);

        var transactions = await db.Context.StorageTransactions
            .Where(x =>
                x.StorageAddressCode == request.StorageAddressCode &&
                x.TransactionDate >= periodStart &&
                x.TransactionDate <= periodEnd &&
                (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                 x.TransactionStatus == StorageTransactionsStatus.Invoiced) &&
                !x.IsInvoiced &&
                (
                    x.ReceiptServicePrice > 0 ||
                    x.ShipmentPrice > 0 ||
                    x.CleaningServicePrice > 0 ||
                    x.DryingServicePrice > 0 ||
                    x.FreightPrice > 0
                ))
            .ToListAsync(ct);

        var charges = await db.Context.StorageCharges
            .Where(x =>
                x.StorageAddressCode == request.StorageAddressCode &&
                x.PeriodEnd >= periodStart &&
                x.PeriodEnd <= periodEnd &&
                !x.IsInvoiced &&
                (
                    x.ChargeType == StorageChargeType.Storage ||
                    x.ChargeType == StorageChargeType.Fumigation ||
                    (request.IncludeUnpricedItems && x.ChargeType == StorageChargeType.TechnicalLoss)
                ))
            .ToListAsync(ct);

        var items = new List<StorageInvoiceItem>();

        foreach (var tx in transactions)
        {
            if (tx.ReceiptServicePrice > 0)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.ReceiptService,
                    Description = $"Recepção - {tx.Code ?? tx.Key.ToString()}",
                    ReferenceDate = tx.TransactionDate!.Value.Date,
                    Quantity = tx.GrossWeight,
                    UnitPriceOrRate = Math.Round(tx.ReceiptServicePrice / tx.GrossWeight, 8) ,
                    TotalAmount = tx.ReceiptServicePrice,
                    TotalQuantityLoss = 0,
                    SourceType = nameof(StorageTransaction),
                    SourceKey = tx.Key,
                    SourceCode = tx.Code
                });
            }

            if (tx.ShipmentPrice > 0)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.ShipmentService,
                    Description = $"Expedição - {tx.Code ?? tx.Key.ToString()}",
                    ReferenceDate = tx.TransactionDate!.Value.Date,
                    Quantity = tx.NetWeight,
                    UnitPriceOrRate = tx.NetWeight == 0 ? 0 : Math.Round(tx.ShipmentPrice / tx.NetWeight, 8),
                    TotalAmount = tx.ShipmentPrice,
                    TotalQuantityLoss = 0,
                    SourceType = nameof(StorageTransaction),
                    SourceKey = tx.Key,
                    SourceCode = tx.Code
                });
            }

            if (tx.CleaningServicePrice > 0)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.CleaningService,
                    Description = $"Pré-limpeza - {tx.Code ?? tx.Key.ToString()}",
                    ReferenceDate = tx.TransactionDate!.Value.Date,
                    Quantity = tx.GrossWeight,
                    UnitPriceOrRate = (tx.CleaningDiscount > 0)
                        ? Math.Round(tx.CleaningServicePrice / tx.GrossWeight, 8)
                        : 0,
                    TotalAmount = tx.CleaningServicePrice,
                    TotalQuantityLoss = 0,
                    SourceType = nameof(StorageTransaction),
                    SourceKey = tx.Key,
                    SourceCode = tx.Code
                });
            }

            if (tx.DryingServicePrice > 0)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.DryingService,
                    Description = $"Secagem - {tx.Code ?? tx.Key.ToString()}",
                    ReferenceDate = tx.TransactionDate!.Value.Date,
                    Quantity = tx.GrossWeight,
                    UnitPriceOrRate = (tx.DryingDiscount > 0)
                        ? Math.Round(tx.DryingServicePrice / tx.GrossWeight, 8)
                        : 0,
                    TotalAmount = tx.DryingServicePrice,
                    TotalQuantityLoss = 0,
                    SourceType = nameof(StorageTransaction),
                    SourceKey = tx.Key,
                    SourceCode = tx.Code
                });
            }
            
            if (tx.FreightPrice > 0)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.DryingService,
                    Description = $"Frete/Remoção - {tx.Code ?? tx.Key.ToString()}",
                    ReferenceDate = tx.TransactionDate!.Value.Date,
                    Quantity = tx.GrossWeight,
                    UnitPriceOrRate = 0,
                    TotalAmount = tx.FreightPrice,
                    TotalQuantityLoss = 0,
                    SourceType = nameof(StorageTransaction),
                    SourceKey = tx.Key,
                    SourceCode = tx.Code
                });
            }
        }

        foreach (var charge in charges)
        {
            if (charge.ChargeType == StorageChargeType.Storage && charge.TotalAmount > 0)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.StorageService,
                    Description = $"Armazenagem {charge.PeriodStart:dd/MM/yyyy} a {charge.PeriodEnd:dd/MM/yyyy}",
                    ReferenceDate = charge.PeriodEnd.Date,
                    Quantity = charge.TonDays,
                    UnitPriceOrRate = charge.UnitPriceOrRate,
                    TotalAmount = charge.TotalAmount,
                    TotalQuantityLoss = 0,
                    SourceType = nameof(StorageCharge),
                    SourceKey = charge.Key,
                    SourceCode = null
                });
            }

            if (charge.ChargeType == StorageChargeType.Fumigation && charge.TotalAmount > 0)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.FumigationService,
                    Description = $"Expurgo {charge.PeriodStart:dd/MM/yyyy} a {charge.PeriodEnd:dd/MM/yyyy}",
                    ReferenceDate = charge.PeriodEnd.Date,
                    Quantity = charge.BaseQuantity,
                    UnitPriceOrRate = charge.UnitPriceOrRate,
                    TotalAmount = charge.TotalAmount,
                    TotalQuantityLoss = 0,
                    SourceType = nameof(StorageCharge),
                    SourceKey = charge.Key
                });
            }

            if (charge.ChargeType == StorageChargeType.TechnicalLoss && charge.TotalQuantityLoss > 0 && request.IncludeUnpricedItems)
            {
                items.Add(new StorageInvoiceItem
                {
                    ItemType = StorageInvoiceItemType.TechnicalLoss,
                    Description = $"Quebra técnica {charge.PeriodStart:dd/MM/yyyy} a {charge.PeriodEnd:dd/MM/yyyy}",
                    ReferenceDate = charge.PeriodEnd.Date,
                    Quantity = charge.BaseQuantity,
                    UnitPriceOrRate = charge.UnitPriceOrRate,
                    TotalAmount = charge.TotalAmount,
                    TotalQuantityLoss = charge.TotalQuantityLoss,
                    SourceType = nameof(StorageCharge),
                    SourceKey = charge.Key
                });
            }
        }

        if (!items.Any())
            throw new BusinessException(resource["NO_ITEMS_TO_INVOICE"]);

        var invoice = new StorageInvoice
        {
            DocNumberKey = request.DocNumberKey,
            Code = await numberSequenceService.GetDocNumber((Guid) request.DocNumberKey),
            BranchCode = address.BranchCode,
            StorageAddressCode = address.Code,
            CardCode = address.CardCode,
            CardName = address.CardName,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ClosingDate = DateTime.Now.Date,
            Status = StorageInvoiceStatus.Open,
            TotalAmount = Math.Round(items.Sum(x => x.TotalAmount), 2, MidpointRounding.AwayFromZero),
            TotalQuantityLoss = items.Sum(x => x.TotalQuantityLoss),
            Notes = request.Notes,
            Items = items
        };

        await db.BeginTransactionAsync();

        try
        {
            db.Context.StorageInvoices.Add(invoice);

            foreach (var item in items)
                item.StorageInvoiceKey = invoice.Key;

            foreach (var tx in transactions)
            {
                var hasAnyInvoiceItem = items.Any(x => x.SourceType == nameof(StorageTransaction) && x.SourceKey == tx.Key);
                if (!hasAnyInvoiceItem)
                    continue;

                tx.IsInvoiced = true;
                tx.StorageInvoiceKey = invoice.Key;
                tx.InvoicedAt = DateTime.Now;
            }

            foreach (var charge in charges)
            {
                var hasAnyInvoiceItem = items.Any(x => x.SourceType == nameof(StorageCharge) && x.SourceKey == charge.Key);
                if (!hasAnyInvoiceItem)
                    continue;

                charge.IsInvoiced = true;
                charge.StorageInvoiceKey = invoice.Key;
            }

            await db.SaveChangesAsync();
            await db.CommitAsync();

            logger.LogInformation(
                "Fatura de armazenagem fechada. Lote: {StorageAddressCode}, Período: {PeriodStart} a {PeriodEnd}, Valor: {TotalAmount}",
                address.Code,
                periodStart,
                periodEnd,
                invoice.TotalAmount);

            return invoice;
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}