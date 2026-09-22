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
/// <para>
/// <b>Regra do discriminador armazém próprio/terceiro (Round 1 da revisão da Task 6): a pergunta
/// certa depende de QUANDO ela é feita, não de uma resposta única para toda a feature.</b>
/// <list type="bullet">
/// <item>
/// <b>Ao CRIAR um transbordo</b> — o ramo <c>isOwn</c> de
/// <c>ShipmentLoadsTransshipmentRegisterEntryService</c> — a pergunta é
/// "este armazém é próprio AGORA?". Aqui <c>WarehouseComplement.IsOwn</c> lido ao vivo é a
/// resposta certa: não existe ainda nenhuma estrutura persistida para consultar, e é exatamente o
/// cadastro atual do armazém que decide que romaneio a entrada vai exigir.
/// </item>
/// <item>
/// <b>Ao decidir sobre um transbordo que JÁ EXISTE</b> — o estorno
/// (<c>ShipmentLoadsTransshipmentReverseService</c>) e o fechamento
/// (<c>ShipmentLoadsAttachTransactionsService.ValidateTransshipmentRoleAsync</c>) — a pergunta é
/// "como esta LINHA foi construída?". Aqui a resposta certa é a estrutura já persistida (o TIPO do
/// romaneio em <see cref="ShipmentLoadTransshipment.EntryStorageTransactionKey"/>: <c>Receipt</c>
/// é próprio, <c>TransshipmentReceipt</c> é terceiro), não <c>WarehouseComplement.IsOwn</c> lido
/// de novo. <c>WarehouseComplementService.SetAsync</c> não trava contra transbordo em aberto — o
/// cadastro pode mudar depois que a linha foi construída, e reconsultar <c>IsOwn</c> ao vivo
/// classificaria uma linha ANTIGA pela configuração NOVA, reabrindo por outra porta o mesmo bug
/// que esta task existe para fechar (liberação e romaneio buscados pela chave errada).
/// </item>
/// </list>
/// A Task 5 chegou a trocar o discriminador de <c>ValidateTransshipmentRoleAsync</c> para
/// <c>IsOwn</c> por medo de dois discriminadores da mesma decisão saírem de sincronia — mas são
/// duas decisões DIFERENTES (criação vs. linha existente), não a mesma pergunta resolvida duas
/// vezes; o Round 1 desta task reverteu essa troca. Não "padronize" isto de volta para
/// <c>IsOwn</c> em todo lugar sem reler este parágrafo primeiro.
/// </para>
/// <para>
/// A fase 1 barrava transbordo em armazém próprio na entrada (<c>EnsureWarehouseAcceptsTransshipmentAsync</c>,
/// removido) porque a mercadoria ficava sem porta de saída; a fase 2 fechou o fluxo inteiro
/// (natureza do lote, crédito na entrada, vínculo da saída, fechamento, estorno e isolamento do
/// lote) e a trava saiu.
/// </para>
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
    /// Vincular a saída do lote (Task 4, fase 2) exige a entrada já registrada — sem ela não
    /// existe lote de onde a mercadoria teria saído. Espelho de
    /// <see cref="EnsureEntryNotAlreadyRegistered"/>, com a checagem invertida.
    /// </summary>
    public static void EnsureEntryIsRegistered(ShipmentLoadTransshipment transshipment)
    {
        if (transshipment.EntryStorageTransactionKey == null)
            throw new ApplicationException(
                $"A entrada do transbordo {transshipment.Sequence} ainda não foi registrada.");
    }

    /// <summary>
    /// Um transbordo só vincula uma saída de lote uma vez —
    /// <see cref="ShipmentLoadTransshipment.LotExitStorageTransactionKey"/> preenchido é o
    /// carimbo de "já vinculada".
    /// </summary>
    public static void EnsureLotExitNotAlreadyAttached(ShipmentLoadTransshipment transshipment)
    {
        if (transshipment.LotExitStorageTransactionKey != null)
            throw new ApplicationException(
                $"O transbordo {transshipment.Sequence} já tem uma saída de lote vinculada.");
    }

    /// <summary>
    /// Fase 2 do GAC-1181, Task 4: o <c>Shipment (1)</c> que a office vincula precisa ser a saída
    /// REAL do MESMO lote que recebeu a entrada — confirmado, do mesmo lote, e ainda sem vínculo
    /// com outra carga ou outro transbordo. Mesmo molde de
    /// <see cref="EnsureOwnWarehouseReceiptIsUsable"/>.
    /// </summary>
    public static void EnsureLotExitIsUsable(
        StorageTransaction lotExit, ShipmentLoadTransshipment transshipment, StorageTransaction entryReceipt)
    {
        if (lotExit.TransactionType != StorageTransactionType.Shipment)
            throw new ApplicationException(
                "O romaneio informado não é uma saída de armazenagem.");

        if (lotExit.TransactionStatus != StorageTransactionsStatus.Confirmed)
            throw new ApplicationException("O romaneio informado não está confirmado.");

        if (!string.Equals(lotExit.StorageAddressCode, entryReceipt.StorageAddressCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O romaneio informado é de outro lote — o transbordo {transshipment.Sequence} " +
                "recebeu a entrada no lote do romaneio de Entrada em Armazenagem vinculado.");

        // Sem os dois nulos, o romaneio já pertence a outra carga ou já fechou outro transbordo —
        // vinculá-lo aqui roubaria a saída de quem já a usa.
        if (lotExit.ShipmentLoadKey != null || lotExit.ShipmentLoadTransshipmentKey != null)
            throw new ApplicationException(
                "O romaneio informado já está vinculado a uma carga ou a outro transbordo.");
    }

    /// <summary>
    /// Fase 2 do GAC-1181: só o <c>Receipt</c> pesado num lote de natureza
    /// <see cref="StorageAddressNature.Transshipment"/> pode registrar a entrada em armazém
    /// próprio — é essa marca (carimbada no cadastro do lote, preservada pela pesagem) que separa
    /// o transbordo de uma Entrada em Armazenagem comum. Sem lote nenhum ou lote
    /// <see cref="StorageAddressNature.Regular"/> caem na mesma recusa.
    /// </summary>
    public static async Task EnsureReceiptIsFromTransshipmentLotAsync(
        AppDbContext context, StorageTransaction receipt)
    {
        var nature = string.IsNullOrWhiteSpace(receipt.StorageAddressCode)
            ? (StorageAddressNature?)null
            : await context.StorageAddresses
                .Where(x => x.Code == receipt.StorageAddressCode)
                .Select(x => (StorageAddressNature?)x.Nature)
                .FirstOrDefaultAsync();

        if (nature != StorageAddressNature.Transshipment)
            throw new ApplicationException(
                "A entrada do transbordo em armazém próprio precisa ser pesada num lote de " +
                "natureza Transbordo. Pese o romaneio no lote correto e tente novamente.");
    }

    /// <summary>
    /// Corta respeitando o VARCHAR(500) das colunas de comentário/motivo do módulo. Único lugar
    /// que faz isso — registrar entrada (Task 5) e estornar (Task 6) chamavam a mesma regra cada
    /// um com sua cópia privada.
    /// </summary>
    public static string Truncate(string value, int max = 500) =>
        value.Length <= max ? value : value[..max];
}
