using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.CustomerReturns;

/// <summary>
/// Linhas de saída elegíveis como ORIGEM de uma devolução do cliente.
///
/// Elegível é a linha que: pertence ao mesmo cliente da devolução, está em documento
/// confirmado (cancelado não devolve nada), teve a entrega CONFERIDA e fechada, e apurou
/// quebra maior que zero. Sem entrega fechada não há quebra apurada — o fator efetivo ainda
/// vale 1 e não há nada que o fiscal precise espelhar.
///
/// O filtro por quebra é feito em SQL pela fórmula, e não pela propriedade calculada
/// <c>AssessedShortage</c>: [NotMapped] não vira SQL.
/// </summary>
public class CustomerReturnsGetOriginItemsService(IUnitOfWork db)
{
    public IQueryable<CustomerReturnOriginItemDto> QueryByCardCode(string cardCode)
    {
        return db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(i =>
                i.SalesInvoice!.CardCode == cardCode &&
                i.SalesInvoice.InvoiceStatus == InvoiceStatus.Confirmed &&
                i.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed &&
                i.Quantity - (i.DeliveredQuantity - i.QuantityLoss) > 0)
            .Select(i => new CustomerReturnOriginItemDto
            {
                SalesInvoiceItemKey = i.Key!.Value,
                InvoiceNumber = i.SalesInvoice!.InvoiceNumber,
                TaxDocumentNumber = i.SalesInvoice.TaxDocumentNumber,
                InvoiceDate = i.SalesInvoice.InvoiceDate,
                ItemCode = i.ItemCode,
                ItemName = i.ItemName,
                UnitOfMeasureCode = i.UnitOfMeasureCode,
                Quantity = i.Quantity,
                DeliveredQuantity = i.DeliveredQuantity,
                QuantityLoss = i.QuantityLoss,
                AssessedShortage = i.Quantity - (i.DeliveredQuantity - i.QuantityLoss),
                SalesContractCode = i.SalesContract!.Code,
            });
    }
}
