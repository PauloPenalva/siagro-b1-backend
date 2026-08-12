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
/// Produto e unidade de medida são derivados da NOTA no servidor, para que a lista case
/// exatamente com os guards de <see cref="SalesContractsReallocationCreateService"/> e o
/// usuário não veja destino que a action vai recusar.
///
/// O <b>cliente</b> é opt-in: por padrão só o cliente da nota, e com
/// <c>includeOtherCustomers</c> a lista abre para todos. Conciliar entre clientes é
/// operação normal desta tela — a conferência com o relatório do cliente revela notas que
/// pertencem ao contrato de outra empresa —, mas abrir isso por padrão deixaria a lista
/// longa e o destino errado a um clique de distância.
/// </summary>
public class SalesContractsGetReconciliationTargetsService(IUnitOfWork db)
{
    public async Task<ICollection<SalesContractReconciliationTargetDto>> ExecuteAsync(
        Guid salesInvoiceItemKey, Guid sourceSalesContractKey, bool includeOtherCustomers = false)
    {
        var item = await db.Context.SalesInvoicesItems
                       .AsNoTracking()
                       .Include(i => i.SalesInvoice)
                       .FirstOrDefaultAsync(i => i.Key == salesInvoiceItemKey)
                   ?? throw new NotFoundException("Item da nota não encontrado.");

        var cardCode = item.SalesInvoice?.CardCode;

        var query = db.Context.SalesContracts
            .AsNoTracking()
            .Where(c => c.Key != sourceSalesContractKey
                        && c.Status != ContractStatus.Finished
                        && (includeOtherCustomers || c.CardCode == cardCode)
                        && c.ItemCode == item.ItemCode
                        && c.UnitOfMeasureCode == item.UnitOfMeasureCode);

        // Com vários clientes na lista, agrupar por cliente é o que a torna legível.
        query = includeOtherCustomers
            ? query.OrderBy(c => c.CardName).ThenBy(c => c.Code)
            : query.OrderBy(c => c.Code);

        return await query
            .Select(c => new SalesContractReconciliationTargetDto
            {
                SalesContractKey = c.Key.ToString(),
                RowId = c.RowId,
                Code = c.Code,
                Complement = c.Complement,
                BranchShortName = c.Branch != null ? c.Branch.ShortName : null,
                CardCode = c.CardCode,
                CardName = c.CardName,
                CardTaxId = c.CardTaxId,
                IsOtherCustomer = c.CardCode != cardCode,
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
