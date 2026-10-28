using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Perna de compra de cada Saída da carga (GAC-1175). A Expedição grava o par Compra (8) + Saída (7)
/// e a carga vincula só a Saída; o contrato de compra, nas Entregas, mostra a Compra. Sem este
/// lookup a carga e o contrato exibem códigos diferentes para o mesmo caminhão.
/// </summary>
/// <remarks>Saída sem perna de compra (transferência de titularidade, devolução) não entra: a tela mostra o próprio código.</remarks>
public class ShipmentLoadsPurchaseLegsService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<ShipmentLoadPurchaseLegDto>> ExecuteAsync(Guid shipmentLoadKey) =>
        await db.Context.ShippingTransactions.AsNoTracking()
            .Where(x => x.PurchaseStorageTransactionKey != null
                        && x.SalesStorageTransaction!.ShipmentLoadKey == shipmentLoadKey)
            .Select(x => new ShipmentLoadPurchaseLegDto
            {
                SalesStorageTransactionKey = x.SalesStorageTransactionKey.ToString(),
                PurchaseStorageTransactionKey = x.PurchaseStorageTransactionKey!.Value.ToString(),
                PurchaseCode = x.PurchaseStorageTransaction!.Code,
            })
            .ToListAsync();
}
