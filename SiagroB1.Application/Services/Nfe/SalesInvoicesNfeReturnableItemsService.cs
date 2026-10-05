using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Itens da venda com vendido, já devolvido e saldo — alimenta o diálogo "Devolver".</summary>
public class SalesInvoicesNfeReturnableItemsService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<SalesInvoiceNfeReturnableItemDto>> ExecuteAsync(Guid key)
    {
        var origin = await db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items)
                         .FirstOrDefaultAsync(i => i.Key == key)
                     ?? throw new NotFoundException("Documento de saída não encontrado.");

        var returned = await SalesInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(db.Context, key, null);

        return SalesInvoiceNfeItemNumbering.Ordered(origin.Items)
            .Select(item =>
            {
                var back = returned.GetValueOrDefault(item.Key!.Value);

                return new SalesInvoiceNfeReturnableItemDto
                {
                    OriginItemKey = item.Key!.Value.ToString(),
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    UnitOfMeasureCode = item.UnitOfMeasureCode,
                    SoldQuantity = (double)item.Quantity,
                    ReturnedQuantity = (double)back,
                    Returnable = (double)Math.Max(0m, item.Quantity - back),
                };
            })
            .ToList();
    }
}
