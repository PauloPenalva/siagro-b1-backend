using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Regras do transbordo (GAC-1181) compartilhadas por iniciar, registrar entrada e estornar.
/// </summary>
/// <remarks>
/// Lançam <see cref="ApplicationException"/> com mensagem de negócio e são chamadas ANTES de abrir
/// transação — o catch dos serviços embrulharia a mensagem.
/// </remarks>
public static class ShipmentLoadTransshipmentRules
{
    public const decimal Tolerance = 0.001m;

    public static void EnsureLoadAcceptsTransshipment(ShipmentLoad load)
    {
        if (load.LoadType == ShipmentLoadType.Removal)
            throw new ApplicationException(
                $"A carga {load.Code} é do tipo Remoção e não tem transbordo.");

        if (load.Status is ShipmentLoadStatus.Cancelled or ShipmentLoadStatus.Returned
            or ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                $"A carga {load.Code} está encerrada e não aceita transbordo.");
    }

    public static void EnsureWarehouseInformed(string? warehouseCode)
    {
        if (string.IsNullOrWhiteSpace(warehouseCode))
            throw new ApplicationException("Informe o armazém do transbordo.");
    }

    /// <summary>
    /// 1, 2, 3… dentro da carga — o próximo número depois do último transbordo já criado.
    /// Único lugar que calcula isso: iniciar (Task 4) e a recusa com destino Transbordo (Task 8)
    /// chamam o mesmo método, para nunca divergirem em "último + 1".
    /// </summary>
    public static async Task<int> NextSequenceAsync(AppDbContext context, Guid shipmentLoadKey)
    {
        var last = await context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .Select(x => (int?)x.Sequence)
            .MaxAsync();

        return (last ?? 0) + 1;
    }

    /// <summary>
    /// Não empilha transbordo sobre transbordo aberto.
    /// </summary>
    /// <remarks>
    /// <see cref="ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync"/> só
    /// enxerga o ÚLTIMO transbordo por <c>Sequence</c> — é assim que o quarto termo do saldo
    /// resolve o status. Se dois pudessem ficar abertos ao mesmo tempo, o mais antigo sumiria da
    /// checagem e o saldo da carga ficaria errado em silêncio. Esta trava é o que impede o
    /// segundo de nascer.
    /// </remarks>
    public static async Task EnsureIsLastAsync(AppDbContext context, ShipmentLoad load)
    {
        if (await ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync(context, load.Key))
            throw new ApplicationException(
                $"A carga {load.Code} já tem um transbordo em aberto. Registre a entrada dele " +
                "antes de iniciar outro.");
    }
}
