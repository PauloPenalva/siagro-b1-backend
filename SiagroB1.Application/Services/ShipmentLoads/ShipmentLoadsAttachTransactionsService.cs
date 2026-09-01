using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
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
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoad> ExecuteAsync(
        Guid shipmentLoadKey,
        ICollection<Guid> storageTransactionKeys,
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

        await ValidateEligibilityAsync(shipments);
        ValidateHomogeneity(load, shipments);

        var attachedQuantity = decimal.Round(
            shipments.Sum(x => x.GrossWeight), 3, MidpointRounding.ToEven);

        try
        {
            await db.BeginTransactionAsync();

            foreach (var shipment in shipments)
            {
                shipment.ShipmentLoadKey = load.Key;
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
    /// Só carga planejada ou aberta recebe romaneio.
    /// </summary>
    /// <remarks>
    /// Vincular a uma carga já faturada aumentaria <see cref="ShipmentLoad.TotalQuantity"/> sob
    /// uma nota emitida — mexendo no denominador que o <c>ShipmentLoadsBillingGuardService</c>
    /// usa durante um faturamento em curso. Pior: o laço de projeção de status do recálculo
    /// carimbaria o romaneio recém-vinculado como <c>Invoiced</c> sem que nada dele tenha sido
    /// faturado.
    /// </remarks>
    private static void EnsureLoadAcceptsShipments(ShipmentLoad load)
    {
        if (load.Status is ShipmentLoadStatus.Planned or ShipmentLoadStatus.Open)
            return;

        var reason = load.Status switch
        {
            ShipmentLoadStatus.Cancelled => "está cancelada",
            ShipmentLoadStatus.PartiallyInvoiced => "já foi faturada parcialmente",
            _ => "já foi faturada",
        };

        throw new ApplicationException(
            $"A carga {load.Code} {reason} e não aceita novos romaneios. " +
            "Cancele os documentos de saída antes de alterar a composição da carga.");
    }

    /// <summary>
    /// Recusa nomeando o <c>Code</c> do romaneio — sem isso o usuário recebe uma negativa que
    /// não diz em qual das linhas selecionadas está o problema.
    /// </summary>
    private async Task ValidateEligibilityAsync(List<StorageTransaction> shipments)
    {
        var invalidType = shipments.FirstOrDefault(x => x.TransactionType != StorageTransactionType.SalesShipment);
        if (invalidType != null)
            throw new ApplicationException(
                $"O documento {invalidType.Code} não é um romaneio de embarque e não pode entrar em uma carga.");

        var notConfirmed = shipments.FirstOrDefault(x => x.TransactionStatus != StorageTransactionsStatus.Confirmed);
        if (notConfirmed != null)
            throw new ApplicationException(
                $"O romaneio {notConfirmed.Code} não está confirmado e não pode entrar em uma carga.");

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
}
