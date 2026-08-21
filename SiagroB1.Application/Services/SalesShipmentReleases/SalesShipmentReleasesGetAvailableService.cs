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
    /// <param name="includeContractsWithoutBalance">
    /// Remove APENAS a cláusula de saldo do CONTRATO, revelando contratos com saldo zero ou
    /// negativo. A cláusula <c>Status == Approved</c> e o filtro de saldo da LIBERAÇÃO
    /// continuam valendo nos dois casos.
    /// <para>
    /// É conveniência de consulta, não autorização: o faturamento <b>não tem guard de saldo de
    /// contrato</b> (ver o <c>&lt;remarks&gt;</c> de
    /// <c>ShipmentBillingCreateSalesInvoiceService</c>), então ligar ou desligar isto não muda
    /// o que o serviço aceita — só o que a lista mostra. O escape existe justamente para o
    /// filtro não virar a trava que aquela decisão removeu: esconder o contrato sem saldo
    /// levaria o usuário a criar um contrato "AJUSTE DE SALDO", que é o desfecho que a
    /// decisão evita.
    /// </para>
    /// </param>
    public IQueryable<SalesShipmentReleaseAvailableDto> Query(
        string itemCode, bool includeContractsWithoutBalance = false)
    {
        var query = db.Context.SalesShipmentReleases
            .Where(r => r.Status == ReleaseStatus.Actived
                        && r.SalesContract != null
                        && r.SalesContract.ItemCode == itemCode
                        && r.SalesContract.Status == ContractStatus.Approved
                        && (r.ReleasedQuantity - r.ShippedQuantity) > 0);

        if (!includeContractsWithoutBalance)
        {
            // Expandido em SQL porque AvaiableVolume é [NotMapped] e o EF não o traduz.
            query = query.Where(r =>
                r.SalesContract!.TotalVolume - r.SalesContract.AllocatedVolume > 0);
        }

        return query
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
                SalesContractStatus = r.SalesContract.Status,
                SalesContractAvailableVolume =
                    r.SalesContract.TotalVolume - r.SalesContract.AllocatedVolume,
            });
    }
}
