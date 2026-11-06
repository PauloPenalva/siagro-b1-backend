using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.Services;

public class StorageInvoicePrintDataService(IUnitOfWork db)
    : IStorageInvoicePrintDataService
{
    public async Task<StorageInvoicePrintDto> GetAsync(Guid storageInvoiceKey, CancellationToken ct = default)
    {
        var invoice = await db.Context.StorageInvoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == storageInvoiceKey, ct)
            ?? throw new NotFoundException("Fatura não encontrada.");

        if (invoice.Status == StorageInvoiceStatus.Cancelled)
        {
            throw new BusinessException("Fatura cancelada.");
        }

        var address = await db.Context.StorageAddresses
            .FirstOrDefaultAsync(x => x.Code == invoice.StorageAddressCode, ct)
            ?? throw new NotFoundException("Lote não encontrado.");

        var dto = new StorageInvoicePrintDto
        {
            InvoiceNumber = invoice.Code,
            StorageAddressCode = address.Code,
            StorageAddressDescription = address.Description,
            CardCode = invoice.CardCode,
            CardName = invoice.CardName ?? "",
            ItemCode = address.ItemCode,
            ItemName = address.ItemName ?? "",
            WarehouseCode = address.WarehouseCode,
            WarehouseName = address.WarehouseName ?? "",
            PeriodStart = invoice.PeriodStart,
            PeriodEnd = invoice.PeriodEnd,
            ClosingDate = invoice.ClosingDate,
            TotalAmount = invoice.TotalAmount,
            TotalQuantityLoss = invoice.TotalQuantityLoss,
            Notes = invoice.Notes,
            Items = invoice.Items
                .OrderBy(x => x.ReferenceDate)
                .ThenBy(x => x.Description)
                .Select(x => new StorageInvoicePrintItemDto
                {
                    ReferenceDate = x.ReferenceDate,
                    Description = x.Description,
                    Quantity = x.Quantity,
                    UnitPriceOrRate = x.UnitPriceOrRate,
                    TotalAmount = x.TotalAmount,
                    TotalQuantityLoss = x.TotalQuantityLoss
                })
                .ToList()
        };
        dto.ServiceSummary = SummarizeByService(dto.Items);
        return dto;
    }

    // "Recepção - 000101" → "Recepção"; "Armazenagem 11/04/2026 a 11/04/2026" → "Armazenagem".
    private static readonly Regex ServiceSuffix = new(@"(\s+-\s+.*|\s+\d{2}/\d{2}/\d{4}.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Total de cada serviço, pelo nome impresso na descrição (o tipo do item não serve:
    /// Frete/Remoção é gravado como Secagem). Ordem da primeira aparição.
    /// </summary>
    public static List<StorageInvoiceServiceSummaryDto> SummarizeByService(IEnumerable<StorageInvoicePrintItemDto> items) =>
        items
            .GroupBy(x => ServiceSuffix.Replace(x.Description, "").Trim())
            .Select(g => new StorageInvoiceServiceSummaryDto
            {
                Service = g.Key,
                Lines = g.Count(),
                Quantity = g.Sum(x => x.Quantity),
                TotalQuantityLoss = g.Sum(x => x.TotalQuantityLoss),
                TotalAmount = g.Sum(x => x.TotalAmount),
            })
            .ToList();
}