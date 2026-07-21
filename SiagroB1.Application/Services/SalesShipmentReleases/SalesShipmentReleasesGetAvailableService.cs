using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesGetAvailableService(IUnitOfWork db)
{
    /// <summary>
    /// Alimenta o dialog de faturamento (<c>/shipment-billing</c>): liberações de venda
    /// <c>Actived</c> com saldo (<c>ReleasedQuantity − ShippedQuantity &gt; 0</c>) para o
    /// produto embarcado, já enriquecidas com dados do contrato (cliente, preço, UoM).
    /// Espelho em SQL da regra <see cref="Domain.Entities.SalesShipmentRelease.AvailableQuantity"/>
    /// (EF não traduz a propriedade [NotMapped]).
    /// </summary>
    public IQueryable<SalesShipmentReleaseAvailableDto> Query(string itemCode)
    {
        return db.Context.SalesShipmentReleases
            .Where(r => r.Status == ReleaseStatus.Actived
                        && r.SalesContract != null
                        && r.SalesContract.ItemCode == itemCode
                        && (r.ReleasedQuantity - r.ShippedQuantity) > 0)
            .OrderByDescending(r => r.RowId)
            .Select(r => new SalesShipmentReleaseAvailableDto
            {
                SalesShipmentReleaseKey = r.Key.ToString(),
                RowId = r.RowId,
                BranchShortName = r.Branch != null ? r.Branch.ShortName : null,
                SalesContractKey = r.SalesContractKey.ToString(),
                SalesContractCode = r.SalesContract!.Code,
                Complement = r.SalesContract.Complement,
                CardCode = r.SalesContract.CardCode,
                CardName = r.SalesContract.CardName,
                CardFName = r.SalesContract.CardFName,
                ItemCode = r.SalesContract.ItemCode,
                ItemName = r.SalesContract.ItemName,
                UnitOfMeasureCode = r.SalesContract.UnitOfMeasureCode,
                Price = r.SalesContract.Price,
                DeliveryLocationCode = r.DeliveryLocationCode,
                DeliveryLocationName = r.DeliveryLocationName,
                AvailableQuantity = r.ReleasedQuantity - r.ShippedQuantity,
            });
    }
}
