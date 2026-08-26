using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Montagem de Carga: agrupa romaneios de embarque soltos num documento próprio.
/// </summary>
/// <remarks>
/// Guarda a invariante I1 — <b>um romaneio, uma carga</b>. A FK escalar
/// <c>StorageTransaction.ShipmentLoadKey</c> é quem torna isso impossível de furar no banco;
/// este serviço é a camada que produz a mensagem acionável, nomeando o romaneio E a carga em
/// que ele já está.
/// <para>
/// A aglutinação exige <c>TruckCode</c> + <c>ItemCode</c> + <c>BranchCode</c> iguais — a mesma
/// regra que a tela de faturamento aplicava à mão antes de a Carga existir.
/// </para>
/// </remarks>
public class ShipmentLoadsCreateService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoad> ExecuteAsync(
        ICollection<Guid> storageTransactionKeys,
        string? comments,
        string userName)
    {
        if (storageTransactionKeys.Count == 0)
            throw new ApplicationException("Selecione ao menos um romaneio de embarque para montar a carga.");

        var distinctKeys = storageTransactionKeys.Distinct().ToList();

        var shipments = await db.Context.StorageTransactions
            .Include(x => x.TruckDriver)
            .Where(x => distinctKeys.Contains(x.Key))
            .ToListAsync();

        if (shipments.Count != distinctKeys.Count)
            throw new ApplicationException("Romaneio de embarque não encontrado.");

        await ValidateEligibilityAsync(shipments);
        ValidateHomogeneity(shipments);

        var first = shipments[0];

        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.ShipmentLoad);

        var load = new ShipmentLoad
        {
            Code = await docNumberSequence.GetDocNumber(docNumberKey),
            DocNumberKey = docNumberKey,
            LoadDate = DateTime.Now.Date,
            Status = ShipmentLoadStatus.Open,
            BranchCode = first.BranchCode,
            ItemCode = first.ItemCode,
            ItemName = first.ItemName,
            UnitOfMeasureCode = first.UnitOfMeasureCode,
            TruckCode = first.TruckCode,
            TruckDriverCode = first.TruckDriverCode,
            TruckDriverName = first.TruckDriver?.Name,
            WarehouseCode = first.WarehouseCode,
            WarehouseName = first.WarehouseName,
            // BRUTO, não líquido: é o número que hoje vira a quantidade da nota. Ver o
            // <remarks> de ShipmentLoad.
            TotalQuantity = decimal.Round(shipments.Sum(x => x.GrossWeight), 3, MidpointRounding.ToEven),
            InvoicedQuantity = decimal.Zero,
            Comments = comments,
            CreatedBy = userName,
            UpdatedBy = userName,
        };

        try
        {
            await db.BeginTransactionAsync();

            db.Context.ShipmentLoads.Add(load);

            foreach (var shipment in shipments)
            {
                shipment.ShipmentLoad = load;
                shipment.UpdatedAt = DateTime.Now;
                shipment.UpdatedBy = userName;
            }

            // Primeiro SaveChanges: é ele que materializa a chave da carga. O movimento
            // precisa dela, e a coluna não tem FK para gravar antes.
            await db.SaveChangesAsync();

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Assembled,
                decimal.Zero,
                load.AvailableQuantity,
                $"Carga montada com {shipments.Count} romaneio(s): " +
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

    private static void ValidateHomogeneity(List<StorageTransaction> shipments)
    {
        if (shipments.Select(x => x.TruckCode).Distinct().Count() > 1)
            throw new ApplicationException("Todos os romaneios da carga devem ser do mesmo veículo.");

        if (shipments.Select(x => x.ItemCode).Distinct().Count() > 1)
            throw new ApplicationException("Todos os romaneios da carga devem ser do mesmo produto.");

        if (shipments.Select(x => x.BranchCode).Distinct().Count() > 1)
            throw new ApplicationException("Todos os romaneios da carga devem ser da mesma filial.");
    }
}
