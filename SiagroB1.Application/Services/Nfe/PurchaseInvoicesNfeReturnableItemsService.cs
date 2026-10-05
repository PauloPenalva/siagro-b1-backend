using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Itens da entrada com comprado, já devolvido e saldo — alimenta o diálogo "Devolver".</summary>
public class PurchaseInvoicesNfeReturnableItemsService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<PurchaseInvoiceNfeReturnableItemDto>> ExecuteAsync(Guid key)
    {
        var origin = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
                         .FirstOrDefaultAsync(i => i.Key == key)
                     ?? throw new NotFoundException("Documento de entrada não encontrado.");

        var returned = await PurchaseInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(db.Context, key, null);

        return NfeItemNumbering.Ordered(origin.Items)
            .Select(item =>
            {
                var back = returned.GetValueOrDefault(item.Key!.Value);

                return new PurchaseInvoiceNfeReturnableItemDto
                {
                    OriginItemKey = item.Key!.Value.ToString(),
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    UnitOfMeasureCode = item.UnitOfMeasureCode,
                    ItemNumber = origin.IssuerType == DocumentIssuerType.ThirdParty
                        ? item.NfeItemNumber
                        : NfeItemNumbering.OriginNumber(item, origin.Items.Count),
                    PurchasedQuantity = (double)item.Quantity,
                    ReturnedQuantity = (double)back,
                    Returnable = (double)Math.Max(0m, item.Quantity - back),
                };
            })
            .ToList();
    }
}
