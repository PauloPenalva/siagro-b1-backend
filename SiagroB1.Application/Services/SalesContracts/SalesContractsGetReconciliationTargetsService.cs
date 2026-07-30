using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Alimenta o dialog de conciliação: contratos de venda candidatos a RECEBER volume de
/// outro contrato.
///
/// Deliberadamente diferente de <c>SalesShipmentReleasesGetAvailableService</c> (que
/// alimenta o faturamento e a realocação operacional): aqui NÃO existe filtro de saldo e
/// NÃO se exige liberação de entrega. Contratos esgotados ou já negativos são exatamente
/// os que precisam aparecer — filtrá-los é o que hoje deixa a conciliação sem saída.
///
/// Cliente, produto e unidade de medida são derivados da NOTA no servidor, para que a
/// lista case exatamente com os guards de <see cref="SalesContractsReallocationCreateService"/>
/// e o usuário não veja destino que a action vai recusar.
/// </summary>
public class SalesContractsGetReconciliationTargetsService(IUnitOfWork db)
{
    public async Task<ICollection<SalesContractReconciliationTargetDto>> ExecuteAsync(
        Guid salesInvoiceItemKey, Guid sourceSalesContractKey)
    {
        var item = await db.Context.SalesInvoicesItems
                       .AsNoTracking()
                       .Include(i => i.SalesInvoice)
                       .FirstOrDefaultAsync(i => i.Key == salesInvoiceItemKey)
                   ?? throw new NotFoundException("Item da nota não encontrado.");

        var cardCode = item.SalesInvoice?.CardCode;

        return await db.Context.SalesContracts
            .AsNoTracking()
            .Where(c => c.Key != sourceSalesContractKey
                        && c.Status != ContractStatus.Finished
                        && c.CardCode == cardCode
                        && c.ItemCode == item.ItemCode
                        && c.UnitOfMeasureCode == item.UnitOfMeasureCode)
            .OrderBy(c => c.Code)
            .Select(c => new SalesContractReconciliationTargetDto
            {
                SalesContractKey = c.Key.ToString(),
                RowId = c.RowId,
                Code = c.Code,
                Complement = c.Complement,
                BranchShortName = c.Branch != null ? c.Branch.ShortName : null,
                CardCode = c.CardCode,
                CardName = c.CardName,
                ItemCode = c.ItemCode,
                ItemName = c.ItemName,
                UnitOfMeasureCode = c.UnitOfMeasureCode,
                HarvestSeasonCode = c.HarvestSeasonCode,
                Price = c.Price,
                TotalVolume = c.TotalVolume,
                AllocatedVolume = c.AllocatedVolume,
                // Espelho em SQL de SalesContract.AvaiableVolume ([NotMapped], não traduz).
                // Pode vir NEGATIVO — é o ponto da tela.
                Balance = c.TotalVolume - c.AllocatedVolume,
                // Mesma regra do FIFO que o create aplica: só liberações ATIVAS com saldo.
                ActiveReleaseBalance = c.SalesShipmentReleases
                    .Where(r => r.Status == ReleaseStatus.Actived)
                    .Sum(r => r.ReleasedQuantity - r.ShippedQuantity),
            })
            .ToListAsync();
    }
}
