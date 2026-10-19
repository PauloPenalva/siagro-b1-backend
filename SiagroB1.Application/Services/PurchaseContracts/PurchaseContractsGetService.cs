using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsGetService(IUnitOfWork unitOfWork, ILogger<PurchaseContractsGetService> logger)
{
    public async Task<PurchaseContract?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await unitOfWork.Context.PurchaseContracts
                .Include(p => p.PriceFixations)
                .Include(p => p.QualityParameters)
                    .ThenInclude(qa => qa.QualityAttrib)
                .Include(p => p.Taxes)
                    .ThenInclude(t => t.Tax)
                .Include(x => x.ShipmentReleases)
                .Include(x => x.HarvestSeason)
                .Include(x => x.LogisticRegion)
                .Include(x => x.DocNumber)
                .Include(x => x.Brokers)
                .Include(x => x.Allocations)
                    .ThenInclude(x => x.StorageTransaction)
                .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<PurchaseContract> QueryAll()
    {
        return unitOfWork.Context.PurchaseContracts
            .AsNoTracking()
            .Include(p => p.Allocations)
                .ThenInclude(a => a.StorageTransaction);
    }

    /// <summary>
    /// Contratos com saldo para alocar um romaneio do produto: qualquer fornecedor (permite corrigir
    /// romaneio lançado com fornecedor ou armazém errado), os do fornecedor do romaneio primeiro.
    /// Tudo no SQL: filtro de saldo, ordem e projeção — sem carregar alocações.
    /// </summary>
    public async Task<List<PurchaseContractDto>> GetAvaiablesPurchaseContractsAsync(string cardCode, string itemCode)
    {
        var list = await AvaiableContracts(itemCode)
            .OrderBy(x => x.CardCode == cardCode ? 0 : 1)
            .ThenBy(x => x.RowId)
            .Select(ToDto)
            .ToListAsync();
        return RoundAvaiableVolume(list);
    }

    /// <summary>
    /// Lista do diálogo de alocação a partir do romaneio. Perna de compra de Expedição só se aloca
    /// no contrato da liberação (decisão 7 do spec): a lista traz só ele, marcado com
    /// <see cref="PurchaseContractDto.IsShippingReleaseContract"/> para o diálogo fazer a alocação
    /// normal, sem o aviso de divergência (o armazém da Expedição é o da liberação). Os demais
    /// romaneios recebem a mesma lista de <see cref="GetAvaiablesPurchaseContractsAsync"/>.
    /// </summary>
    public async Task<List<PurchaseContractDto>> GetAvaiablesPurchaseContractsForTransactionAsync(
        Guid storageTransactionKey)
    {
        var transaction = await unitOfWork.Context.StorageTransactions
                              .AsNoTracking()
                              .Where(x => x.Key == storageTransactionKey)
                              .Select(x => new { x.CardCode, x.ItemCode })
                              .FirstOrDefaultAsync()
                          ?? throw new NotFoundException("Romaneio não encontrado.");

        var releaseContractKey = await ShippingPurchaseLegRules.ReleaseContractKeyAsync(
            unitOfWork.Context, storageTransactionKey);
        if (releaseContractKey == null)
            return await GetAvaiablesPurchaseContractsAsync(transaction.CardCode, transaction.ItemCode);

        var list = await AvaiableContracts(transaction.ItemCode)
            .Where(x => x.Key == releaseContractKey)
            .Select(ToDto)
            .ToListAsync();
        foreach (var dto in list)
            dto.IsShippingReleaseContract = true;
        return RoundAvaiableVolume(list);
    }

    /// <summary>
    /// Contratos aprovados/encerrados do produto com <see cref="PurchaseContract.AvaiableVolume"/> &gt; 0.
    /// AvaiableVolume = Round(TotalVolume − AllocatedVolume − WashedOutVolume, 2, ToEven), só colunas
    /// persistidas. Com 3 casas no banco, o arredondado é &gt; 0 exatamente quando a diferença é
    /// &gt; 0,005 (0,005 arredonda para 0,00 no ToEven) — assim o filtro vai para o SQL sem mudar a regra.
    /// </summary>
    private IQueryable<PurchaseContract> AvaiableContracts(string itemCode) =>
        unitOfWork.Context.PurchaseContracts
            .AsNoTracking()
            .Where(p => p.ItemCode == itemCode &&
                        (p.Status == ContractStatus.Approved || p.Status == ContractStatus.Finished) &&
                        p.TotalVolume - p.AllocatedVolume - p.WashedOutVolume > 0.005m);

    // Saldo sai cru do SQL (TotalVolume − AllocatedVolume − WashedOutVolume); RoundAvaiableVolume
    // aplica o mesmo arredondamento de PurchaseContract.AvaiableVolume em memória.
    private static readonly System.Linq.Expressions.Expression<Func<PurchaseContract, PurchaseContractDto>> ToDto =
        x => new PurchaseContractDto
        {
            Key = x.Key,
            Code = x.Code,
            AvaiableVolume = x.TotalVolume - x.AllocatedVolume - x.WashedOutVolume,
            UnitOfMeasureCode = x.UnitOfMeasureCode,
            AgentCode = x.AgentCode,
            AgentName = x.AgentName,
            CardCode = x.CardCode,
            CardName = x.CardName,
            DeliveryLocationCode = x.DeliveryLocationCode,
            DeliveryLocationName = x.DeliveryLocationName,
        };

    private static List<PurchaseContractDto> RoundAvaiableVolume(List<PurchaseContractDto> list)
    {
        foreach (var dto in list)
            dto.AvaiableVolume = decimal.Round(dto.AvaiableVolume, 2, MidpointRounding.ToEven);
        return list;
    }
}
