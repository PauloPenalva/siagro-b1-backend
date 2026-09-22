using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
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

        // Completed é defensivo e hoje inalcançável por aqui: a única carga que chega a
        // Completed é a de Remoção (ShipmentLoadsCompleteService), e ela já foi recusada acima
        // pelo LoadType. Mantido para o dia em que outro tipo de carga ganhar um ciclo terminal
        // próprio.
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
    /// Fase 1 do GAC-1181: transbordo em armazém próprio ainda não tem porta de saída. O ramo que
    /// já existe (<see cref="EnsureOwnWarehouseReceiptIsUsable"/>, usado por
    /// <c>ShipmentLoadsTransshipmentRegisterEntryService</c>) só VINCULA um <c>Receipt</c> já
    /// lançado pela Entrada em Armazenagem — não credita o saldo do ARMAZÉM (só o do lote) e não
    /// emite nenhuma liberação, então a mercadoria fica sem porta de saída: o operador só
    /// embarcaria consumindo uma liberação de outro negócio, corrompendo o saldo dele. Verificado
    /// com dado real. A fase 2 (lote de natureza "Transbordo", entrada e saída pela pesagem) está
    /// desenhada em <c>docs/superpowers/specs/2026-09-21-gac-1181-load-transshipment-design.md</c>,
    /// não implementada.
    /// <para>
    /// Barrado aqui, na ENTRADA do fluxo (<c>ShipmentLoadsTransshipmentStartService</c> e o ramo
    /// Transbordo de <c>ShipmentLoadsRefuseService</c>), e não removendo o ramo de
    /// <c>RegisterEntryService</c>: ele é o alicerce da fase 2 e continua exercitado por teste
    /// direto.
    /// </para>
    /// </summary>
    public static async Task EnsureWarehouseAcceptsTransshipmentAsync(
        IWarehouseComplementService complements, string warehouseCode)
    {
        var complement = await complements.GetAsync(warehouseCode);

        if (complement?.IsOwn == true)
            throw new ApplicationException(
                "Transbordo em armazém próprio ainda não está disponível nesta versão. " +
                "Escolha um armazém de terceiro.");
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
    /// <para>
    /// Chamada tanto por quem INICIA um transbordo (Task 4,
    /// <c>ShipmentLoadsTransshipmentStartService</c>) quanto pela recusa com destino Transbordo
    /// (Task 8, <c>ShipmentLoadsRefuseService</c>) — é a MESMA invariante nos dois casos: um
    /// transbordo aberto de qualquer origem bloqueia o próximo, e a recusa PARCIAL repetida é
    /// justamente como esse segundo nasceria sem esta trava.
    /// </para>
    /// </remarks>
    public static async Task EnsureIsLastAsync(AppDbContext context, ShipmentLoad load)
    {
        if (await ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync(context, load.Key))
            throw new ApplicationException(
                $"A carga {load.Code} já tem um transbordo em aberto. Registre a entrada dele " +
                "ou vincule a saída antes de abrir outro transbordo.");
    }

    /// <summary>
    /// Um transbordo só registra a entrada uma vez — <see cref="ShipmentLoadTransshipment
    /// .EntryStorageTransactionKey"/> preenchido é o carimbo de "já registrado". Corrigir exige
    /// estornar primeiro (Task 6).
    /// </summary>
    public static void EnsureEntryNotAlreadyRegistered(ShipmentLoadTransshipment transshipment)
    {
        if (transshipment.EntryStorageTransactionKey != null)
            throw new ApplicationException(
                $"A entrada do transbordo {transshipment.Sequence} já foi registrada. Estorne-o " +
                "para corrigir.");
    }

    /// <summary>
    /// Armazém de terceiro: o peso pesado na entrada precisa ser positivo e não pode superar o
    /// que saiu da carga — a mesma tolerância de arredondamento usada no resto do módulo.
    /// </summary>
    public static void EnsureThirdPartyQuantityIsValid(
        decimal quantity, ShipmentLoadTransshipment transshipment)
    {
        if (quantity <= Tolerance)
            throw new ApplicationException("Informe o peso pesado na entrada do transbordo.");

        if (quantity > transshipment.OutgoingQuantity + Tolerance)
            throw new ApplicationException(
                $"O peso informado ({quantity:N3}) é maior que o volume transbordado " +
                $"({transshipment.OutgoingQuantity:N3}).");
    }

    /// <summary>
    /// Armazém próprio: a entrada não nasce aqui — já existe, lançada pela tela de Entrada em
    /// Armazenagem de sempre. Esta checagem só admite VINCULAR um <c>Receipt</c> que realmente
    /// corresponde a esta carga e ainda não pertence a nada.
    /// </summary>
    public static void EnsureOwnWarehouseReceiptIsUsable(
        StorageTransaction receipt, ShipmentLoad load, ShipmentLoadTransshipment transshipment)
    {
        if (receipt.TransactionType != StorageTransactionType.Receipt)
            throw new ApplicationException(
                "O romaneio informado não é uma Entrada em Armazenagem.");

        if (receipt.TransactionStatus != StorageTransactionsStatus.Confirmed)
            throw new ApplicationException("O romaneio informado não está confirmado.");

        if (!string.Equals(receipt.WarehouseCode, transshipment.WarehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException("O romaneio informado é de outro armazém.");

        if (!string.Equals(receipt.ItemCode, load.ItemCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.BranchCode, load.BranchCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.UnitOfMeasureCode, load.UnitOfMeasureCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationException(
                "O romaneio informado não corresponde ao produto, filial ou unidade da carga.");
        }

        // Sem os dois nulos, o romaneio já pertence a outra carga ou já fechou outro transbordo —
        // vinculá-lo aqui roubaria a entrada de quem já a usa.
        if (receipt.ShipmentLoadKey != null || receipt.ShipmentLoadTransshipmentKey != null)
            throw new ApplicationException(
                "O romaneio informado já está vinculado a uma carga ou a outro transbordo.");
    }

    /// <summary>
    /// Corta respeitando o VARCHAR(500) das colunas de comentário/motivo do módulo. Único lugar
    /// que faz isso — registrar entrada (Task 5) e estornar (Task 6) chamavam a mesma regra cada
    /// um com sua cópia privada.
    /// </summary>
    public static string Truncate(string value, int max = 500) =>
        value.Length <= max ? value : value[..max];
}
