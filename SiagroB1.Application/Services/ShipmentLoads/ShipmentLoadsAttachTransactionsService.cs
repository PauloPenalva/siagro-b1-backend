using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Vincula romaneios de embarque a uma carga já planejada. É o segundo passo do fluxo: a
/// Logística cria a carga, o carregamento acontece, e os romaneios entram aqui.
/// </summary>
/// <remarks>
/// Guarda a invariante I1 — <b>um romaneio, uma carga</b>. Quem torna isso impossível de furar
/// é a FK escalar <c>StorageTransaction.ShipmentLoadKey</c>; este serviço é a camada que produz
/// a mensagem acionável, nomeando o romaneio E a carga em que ele já está.
/// <para>
/// A homogeneidade (<c>TruckCode</c> + <c>ItemCode</c> + <c>BranchCode</c> + unidade) é
/// comparada contra a CARGA, não entre os romaneios selecionados. Essa é a diferença em relação
/// ao fluxo antigo, em que a carga herdava os atributos do primeiro romaneio e a comparação só
/// podia ser entre iguais. <c>WarehouseCode</c> e cliente NÃO entram na comparação: são
/// informativos por decisão de negócio — as expedições é que provêm a informação correta.
/// </para>
/// </remarks>
public class ShipmentLoadsAttachTransactionsService(
    IUnitOfWork db,
    IWarehouseComplementService warehouseComplements,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoad> ExecuteAsync(
        Guid shipmentLoadKey,
        ICollection<Guid> storageTransactionKeys,
        Guid? transshipmentKey,
        string userName)
    {
        if (storageTransactionKeys.Count == 0)
            throw new ApplicationException("Selecione ao menos um romaneio de embarque para vincular.");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == shipmentLoadKey) ??
                   throw new NotFoundException($"Shipment load not found key {shipmentLoadKey}");

        EnsureLoadAcceptsShipments(load);

        var distinctKeys = storageTransactionKeys.Distinct().ToList();

        var shipments = await db.Context.StorageTransactions
            .Where(x => distinctKeys.Contains(x.Key))
            .ToListAsync();

        if (shipments.Count != distinctKeys.Count)
            throw new ApplicationException("Romaneio de embarque não encontrado.");

        await ValidateEligibilityAsync(load, shipments);
        ValidateHomogeneity(load, shipments);

        // GAC-1181: com a chave, a saída assume o PAPEL de saída do transbordo; sem ela, segue o
        // caminho de sempre — mas barrado se o armazém for de um transbordo já recebido, que só
        // pode ser vinculado pelo outro caminho.
        var transshipment = transshipmentKey is { } key
            ? await ValidateTransshipmentRoleAsync(load, key, shipments)
            : await EnsureNoneIsATransshipmentWarehouseAsync(load, shipments);

        var attachedQuantity = decimal.Round(
            shipments.Sum(x => x.GrossWeight), 3, MidpointRounding.ToEven);

        try
        {
            await db.BeginTransactionAsync();

            foreach (var shipment in shipments)
            {
                shipment.ShipmentLoadKey = load.Key;
                shipment.ShipmentLoadTransshipmentKey = transshipment?.Key;
                shipment.UpdatedAt = DateTime.Now;
                shipment.UpdatedBy = userName;
            }

            // O recálculo do total CONSULTA os romaneios da carga, então as FKs precisam estar
            // gravadas antes — senão ele soma o conjunto anterior.
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateTotalService.RecalculateAsync(db.Context, load.Key);
            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedBy = userName;

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransactionsAttached,
                attachedQuantity,
                load.AvailableQuantity,
                $"{shipments.Count} romaneio(s) vinculado(s) à carga: " +
                string.Join(", ", shipments.Select(x => x.Code)),
                userName);

            await db.SaveChangesAsync();

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return load;
    }

    /// <summary>
    /// Carga planejada, aberta ou EM TRANSBORDO (GAC-1181) recebe romaneio.
    /// </summary>
    /// <remarks>
    /// Vincular a uma carga já faturada aumentaria <see cref="ShipmentLoad.TotalQuantity"/> sob
    /// uma nota emitida — mexendo no denominador que o <c>ShipmentLoadsBillingGuardService</c>
    /// usa durante um faturamento em curso. Pior: o laço de projeção de status do recálculo
    /// carimbaria o romaneio recém-vinculado como <c>Invoiced</c> sem que nada dele tenha sido
    /// faturado.
    /// <para>
    /// <b><see cref="ShipmentLoadStatus.InTransshipment"/> é aceito de propósito</b>: é
    /// exatamente o status da carga no momento em que a saída do transbordo precisa ser
    /// vinculada — o transbordo descarregou o saldo INTEIRO, então a carga fica nesse status até
    /// essa saída "fechar" o transbordo (<see cref="ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync"/>).
    /// Quem impede o abuso desta abertura é <see cref="ValidateTransshipmentRoleAsync"/> e
    /// <see cref="EnsureNoneIsATransshipmentWarehouseAsync"/>, não este guard.
    /// </para>
    /// </remarks>
    private static void EnsureLoadAcceptsShipments(ShipmentLoad load)
    {
        if (load.Status is ShipmentLoadStatus.Planned or ShipmentLoadStatus.Open
            or ShipmentLoadStatus.InTransshipment)
            return;

        var reason = load.Status switch
        {
            ShipmentLoadStatus.Cancelled => "está cancelada",
            ShipmentLoadStatus.PartiallyInvoiced => "já foi faturada parcialmente",
            ShipmentLoadStatus.Completed =>
                "já foi concluída — reabra-a antes de alterar a composição",
            _ => "já foi faturada",
        };

        throw new ApplicationException(
            $"A carga {load.Code} {reason} e não aceita novos romaneios. " +
            (load.LoadType == ShipmentLoadType.Removal
                ? string.Empty
                : "Cancele os documentos de saída antes de alterar a composição da carga."));
    }

    /// <summary>
    /// Tipo de romaneio que cada natureza de carga aceita (GAC-1175).
    /// </summary>
    /// <remarks>
    /// A carga Normal transporta mercadoria para FORA (embarque de venda); a de Remoção traz
    /// mercadoria para DENTRO de um armazém, e o documento disso é o Recebimento. São os dois
    /// únicos tipos que podem carregar <c>ShipmentLoadKey</c>.
    /// </remarks>
    public static StorageTransactionType ExpectedTransactionType(ShipmentLoadType loadType) =>
        loadType == ShipmentLoadType.Removal
            ? StorageTransactionType.Receipt
            : StorageTransactionType.SalesShipment;

    /// <summary>
    /// Recusa nomeando o <c>Code</c> do romaneio — sem isso o usuário recebe uma negativa que
    /// não diz em qual das linhas selecionadas está o problema.
    /// </summary>
    private async Task ValidateEligibilityAsync(ShipmentLoad load, List<StorageTransaction> shipments)
    {
        var expectedType = ExpectedTransactionType(load.LoadType);

        var invalidType = shipments.FirstOrDefault(x => x.TransactionType != expectedType);
        if (invalidType != null)
        {
            var expected = load.LoadType == ShipmentLoadType.Removal
                ? "um romaneio de recebimento"
                : "um romaneio de embarque";

            throw new ApplicationException(
                $"O documento {invalidType.Code} não é {expected} e não pode entrar na carga {load.Code}.");
        }

        var notConfirmed = shipments.FirstOrDefault(x => x.TransactionStatus != StorageTransactionsStatus.Confirmed);
        if (notConfirmed != null)
            throw new ApplicationException(
                $"O romaneio {notConfirmed.Code} não está confirmado e não pode entrar em uma carga.");

        // Expedição ORIGINAL substituída por uma troca de liberação (GAC-1177): quem controla
        // esse romaneio dali em diante é o fluxo da troca, não a Montagem de Carga.
        var replaced = shipments.FirstOrDefault(x => x.ReplacedByShippingReleaseChangeKey != null);
        if (replaced != null)
            throw new ApplicationException(
                $"O romaneio {replaced.Code} faz parte de uma troca de liberação e não pode ser vinculado a uma carga.");

        var alreadyLoaded = shipments.FirstOrDefault(x => x.ShipmentLoadKey != null);
        if (alreadyLoaded == null)
            return;

        var loadCode = await db.Context.ShipmentLoads
            .Where(x => x.Key == alreadyLoaded.ShipmentLoadKey)
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

        throw new ApplicationException(
            $"O romaneio {alreadyLoaded.Code} já está montado na carga {loadCode}.");
    }

    private static void ValidateHomogeneity(ShipmentLoad load, List<StorageTransaction> shipments)
    {
        var wrongTruck = shipments.FirstOrDefault(x => x.TruckCode != load.TruckCode);
        if (wrongTruck != null)
            throw new ApplicationException(
                $"O romaneio {wrongTruck.Code} é do veículo {wrongTruck.TruckCode} e a carga " +
                $"{load.Code} é do veículo {load.TruckCode}.");

        var wrongItem = shipments.FirstOrDefault(x => x.ItemCode != load.ItemCode);
        if (wrongItem != null)
            throw new ApplicationException(
                $"O romaneio {wrongItem.Code} é do produto {wrongItem.ItemCode} e a carga " +
                $"{load.Code} é do produto {load.ItemCode}.");

        var wrongBranch = shipments.FirstOrDefault(x => x.BranchCode != load.BranchCode);
        if (wrongBranch != null)
            throw new ApplicationException(
                $"O romaneio {wrongBranch.Code} é da filial {wrongBranch.BranchCode} e a carga " +
                $"{load.Code} é da filial {load.BranchCode}.");

        // A soma de GrossWeight é adimensional: misturar unidades produziria um TotalQuantity
        // sem significado, e é ele que vira a quantidade da nota.
        var wrongUom = shipments.FirstOrDefault(x => x.UnitOfMeasureCode != load.UnitOfMeasureCode);
        if (wrongUom != null)
            throw new ApplicationException(
                $"O romaneio {wrongUom.Code} está em {wrongUom.UnitOfMeasureCode} e a carga " +
                $"{load.Code} está em {load.UnitOfMeasureCode}.");
    }

    /// <summary>
    /// GAC-1181: vincula por PAPEL — a saída que recarrega no armazém intermediário e devolve o
    /// volume transbordado à carga. Exige o transbordo desta carga, com entrada já registrada
    /// (é ela que "recebeu" a mercadoria e libera a recarga), e todo romaneio selecionado do
    /// armazém do transbordo.
    /// </summary>
    private async Task<ShipmentLoadTransshipment> ValidateTransshipmentRoleAsync(
        ShipmentLoad load, Guid transshipmentKey, List<StorageTransaction> shipments)
    {
        var transshipment = await db.Context.ShipmentLoadsTransshipments
                                 .FirstOrDefaultAsync(x => x.Key == transshipmentKey) ??
                             throw new NotFoundException(
                                 $"Shipment load transshipment not found key {transshipmentKey}");

        if (transshipment.ShipmentLoadKey != load.Key)
            throw new ApplicationException(
                $"O transbordo informado não pertence à carga {load.Code}.");

        if (transshipment.EntryStorageTransactionKey == null)
            throw new ApplicationException(
                $"A entrada do transbordo {transshipment.Sequence} ainda não foi registrada.");

        // GAC-1181 fase 2: em armazém PRÓPRIO a liberação só nasce quando a saída do LOTE é
        // vinculada depois (ShipmentLoadsTransshipmentAttachLotExitService, Task 4) — sem isso,
        // esta Expedição só poderia estar consumindo liberação de outro negócio, o buraco que esta
        // fase existe para fechar. Em armazém de terceiro a entrada já É o TransshipmentReceipt
        // (15), que emite a liberação sozinho na Task 3 (ShipmentLoadsTransshipmentRegisterEntryService),
        // então esta checagem não se aplica. O mesmo discriminador (WarehouseComplement.IsOwn) já
        // decide essa mesma pergunta em ShipmentLoadTransshipmentRules.EnsureWarehouseAcceptsTransshipmentAsync
        // e em ShipmentLoadsTransshipmentRegisterEntryService — reusado aqui em vez de inferir pelo
        // TIPO do romaneio de entrada, para não ter dois discriminadores da mesma decisão que
        // possam sair de sincronia sem ninguém notar.
        var complement = await warehouseComplements.GetAsync(transshipment.WarehouseCode);

        if (complement?.IsOwn == true && transshipment.LotExitStorageTransactionKey == null)
        {
            throw new ApplicationException(
                $"O transbordo {transshipment.Sequence} ainda não tem a saída do lote vinculada. " +
                "Vincule a saída do lote antes de vincular esta Expedição.");
        }

        var notShipment = shipments.FirstOrDefault(x => x.TransactionType != StorageTransactionType.SalesShipment);
        if (notShipment != null)
            throw new ApplicationException(
                $"O romaneio {notShipment.Code} não é uma saída de embarque e não pode ser " +
                "vinculado como saída de um transbordo.");

        var wrongWarehouse = shipments.FirstOrDefault(x =>
            !string.Equals(x.WarehouseCode, transshipment.WarehouseCode, StringComparison.OrdinalIgnoreCase));
        if (wrongWarehouse != null)
            throw new ApplicationException(
                $"O romaneio {wrongWarehouse.Code} é do armazém {wrongWarehouse.WarehouseCode} e o " +
                $"transbordo é no armazém {transshipment.WarehouseCode}.");

        return transshipment;
    }

    /// <summary>
    /// Sem a chave, recusa vincular um romaneio cujo armazém seja o de um transbordo desta carga
    /// COM ENTRADA REGISTRADA: ele é a saída daquele transbordo disfarçada de vinculação comum, e
    /// entrar por aqui deixaria a carga sem o carimbo que fecha o transbordo.
    /// </summary>
    /// <remarks>
    /// Revisão da Task 7: dois transbordos da MESMA carga podem compartilhar o
    /// <c>WarehouseCode</c> (um encerrado, outro reaberto depois no mesmo armazém parceiro). Um
    /// <c>FirstOrDefault</c> citaria só o primeiro da lista, arriscando nomear o transbordo
    /// ERRADO na mensagem. Ambíguo, a mensagem lista TODOS os candidatos daquele armazém em vez
    /// de adivinhar; só nomeia um número quando ele é o único.
    /// </remarks>
    private async Task<ShipmentLoadTransshipment?> EnsureNoneIsATransshipmentWarehouseAsync(
        ShipmentLoad load, List<StorageTransaction> shipments)
    {
        var registered = await db.Context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == load.Key && x.EntryStorageTransactionKey != null)
            .ToListAsync();

        if (registered.Count == 0)
            return null;

        foreach (var shipment in shipments)
        {
            var matches = registered
                .Where(x => string.Equals(
                    x.WarehouseCode, shipment.WarehouseCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Sequence)
                .ToList();

            if (matches.Count == 0)
                continue;

            if (matches.Count == 1)
                throw new ApplicationException(
                    $"O romaneio {shipment.Code} é do armazém do transbordo {matches[0].Sequence} " +
                    $"desta carga. Vincule-o como saída do transbordo {matches[0].Sequence}.");

            var sequences = string.Join(", ", matches.Select(x => x.Sequence));

            throw new ApplicationException(
                $"O romaneio {shipment.Code} é do armazém de mais de um transbordo desta carga " +
                $"({sequences}). Vincule-o pela tela de transbordo, informando o transbordo certo.");
        }

        return null;
    }
}
