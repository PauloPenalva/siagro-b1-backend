using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Diagnóstico da tela de conciliação: contratos de venda com saldo por nota NEGATIVO
/// (<c>TotalVolume − AllocatedVolume &lt; 0</c>) — o sintoma dos contratos legados,
/// faturados por fora da liberação de entrega.
///
/// ⚠️ <c>AllocatedVolume</c> é persistido-derivado: parte dos negativos pode ser drift do
/// agregado, não erro de distribuição. Rodar antes a action
/// <c>SalesContractsRecalculateAllBalances</c> separa um caso do outro — é por isso que a
/// tela oferece o botão de recálculo ao lado desta lista.
///
/// Cliente e produto vêm dos campos DESNORMALIZADOS do próprio contrato (CardName /
/// ItemName), nunca de navegação para BUSINESS_PARTNERS/ITEMS: em modo SAPB1 essas
/// tabelas locais estão vazias e o JOIN zeraria a lista inteira.
/// </summary>
public class SalesContractsGetNegativeBalancesService(IUnitOfWork db)
{
    public async Task<ICollection<SalesContractNegativeBalanceDto>> ExecuteAsync()
    {
        return await db.Context.SalesContracts
            .AsNoTracking()
            .Where(c => c.Status != ContractStatus.Finished
                        && c.TotalVolume - c.AllocatedVolume < 0)
            .OrderBy(c => c.TotalVolume - c.AllocatedVolume)
            .Select(c => new SalesContractNegativeBalanceDto
            {
                SalesContractKey = c.Key.ToString(),
                RowId = c.RowId,
                Code = c.Code,
                Complement = c.Complement,
                BranchCode = c.BranchCode,
                BranchName = c.Branch != null ? c.Branch.ShortName : null,
                CardCode = c.CardCode,
                CardName = c.CardName,
                ItemCode = c.ItemCode,
                ItemName = c.ItemName,
                UnitOfMeasureCode = c.UnitOfMeasureCode,
                HarvestSeasonCode = c.HarvestSeasonCode,
                TotalVolume = c.TotalVolume,
                AllocatedVolume = c.AllocatedVolume,
                Balance = c.TotalVolume - c.AllocatedVolume,
            })
            .ToListAsync();
    }
}
