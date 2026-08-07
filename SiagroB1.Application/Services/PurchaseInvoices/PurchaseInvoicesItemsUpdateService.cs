using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Altera uma linha do documento de entrada — inclusive a AMARRAÇÃO com a nota de origem, que é o
/// que a grade edita depois de importar o XML.
///
/// A guarda de status é a do documento PAI, e é lida da linha existente e não da entrante: um
/// PATCH parcial não traz <c>PurchaseInvoiceKey</c>.
/// </summary>
public class PurchaseInvoicesItemsUpdateService(IUnitOfWork db, IItemService itemService)
{
    public async Task ExecuteAsync(Guid key, PurchaseInvoiceItem entity, string userName)
    {
        var existing = await db.Context.PurchaseInvoicesItems
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Linha do documento de entrada não encontrada.");

        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, existing.PurchaseInvoiceKey);

        // É por AQUI que a grade do Edit troca o produto: ela faz PATCH na LINHA
        // (`PurchaseInvoicesItems({key})`), não no cabeçalho, então o SyncItems do
        // PurchaseInvoicesUpdateService nunca vê essa alteração.
        //
        // Capturado ANTES da atribuição, e comparando com o EXISTENTE: o value help grava a
        // descrição com group ID null, então o PATCH chega só com `ItemCode` e a descrição que
        // sobra no `entity` é a do produto ANTERIOR. Sem isto a linha ficava com o código de um
        // produto e o nome de outro — verificado no navegador.
        //
        // Comparar `existing` com `entity` só é válido porque o GetService é AsNoTracking: se
        // viesse rastreado seriam O MESMO objeto e a comparação daria sempre falso.
        var itemCodeChanged = existing.ItemCode != entity.ItemCode;

        // Linha sem contrato (caso comum: insumo, serviço, frete) não precisa do CardCode do pai —
        // pula a query e a chamada ao guard.
        if (entity.PurchaseContractKey is not null)
        {
            var cardCode = await db.Context.PurchaseInvoices
                .Where(x => x.Key == existing.PurchaseInvoiceKey)
                .Select(x => x.CardCode)
                .FirstAsync();

            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, entity.PurchaseContractKey, entity.ItemCode, cardCode);
        }

        existing.ItemCode = entity.ItemCode;
        existing.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
            itemService, entity.ItemCode, entity.ItemName, itemCodeChanged);
        existing.Quantity = entity.Quantity;
        existing.UnitPrice = entity.UnitPrice;
        existing.UnitOfMeasureCode = entity.UnitOfMeasureCode;
        existing.SalesInvoiceItemKey = entity.SalesInvoiceItemKey;
        existing.PurchaseInvoiceItemOriginKey = entity.PurchaseInvoiceItemOriginKey;
        existing.PurchaseContractKey = entity.PurchaseContractKey;

        await db.SaveChangesAsync();
    }
}
