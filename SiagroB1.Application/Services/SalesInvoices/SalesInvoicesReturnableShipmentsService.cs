using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Romaneios de um documento de saída LEGADO que ainda podem ser devolvidos. É a fonte da grade
/// do diálogo de retorno.
/// </summary>
/// <remarks>
/// <b>Só romaneios de embarque ainda FATURADOS.</b> <c>Returned</c> é o que sobra de um retorno
/// parcial anterior e oferecê-lo devolveria o mesmo volume duas vezes; <c>Cancelled</c> não
/// existe mais; <c>Confirmed</c> não pertence a nota nenhuma.
/// <para>
/// O escopo é <c>SalesInvoiceKey</c>, e nunca um casamento por cliente/produto: é exatamente o
/// critério largo da consulta de "órfãos" de <c>SalesInvoicesReverseConfirmService</c> que já
/// sequestrou romaneio alheio uma vez.
/// </para>
/// </remarks>
public class SalesInvoicesReturnableShipmentsService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<SalesInvoiceReturnableShipmentDto>> ExecuteAsync(
        Guid salesInvoiceKey)
    {
        return await db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == salesInvoiceKey &&
                        x.TransactionType == StorageTransactionType.SalesShipment &&
                        x.TransactionStatus == StorageTransactionsStatus.Invoiced)
            .OrderBy(x => x.Code)
            .Select(x => new SalesInvoiceReturnableShipmentDto
            {
                StorageTransactionKey = x.Key.ToString(),
                Code = x.Code,
                TransactionDate = x.TransactionDate,
                TruckCode = x.TruckCode,
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                UnitOfMeasureCode = x.UnitOfMeasureCode,
                WarehouseCode = x.WarehouseCode,
                WarehouseName = x.WarehouseName,
                GrossWeight = x.GrossWeight,
                NetWeight = x.NetWeight,
            })
            .ToListAsync();
    }
}
