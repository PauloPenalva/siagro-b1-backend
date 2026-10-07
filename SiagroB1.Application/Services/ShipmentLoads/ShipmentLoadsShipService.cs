using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

public record ShipmentLoadShipRequest(
    Guid ShipmentLoadKey,
    Guid ShipmentReleaseKey,
    string WarehouseCode,
    string TruckDriverCode,
    DateTime TransactionDate,
    decimal GrossWeight,
    string? Comments);

public record ShipmentLoadShipResult(Guid ShipmentLoadKey, Guid StorageTransactionKey, string? StorageTransactionCode);

/// <summary>
/// "Expedir" no detalhe da carga (spec 2026-10-06 peça 2): cria o par de romaneios da Expedição de Grãos e vincula a
/// saída à carga numa transação só — ou o romaneio nasce vinculado, ou nada é gravado.
/// </summary>
/// <remarks>
/// Placa, produto, filial e unidade vêm da CARGA (a homogeneidade do vínculo passa por construção); o parceiro vem do
/// contrato da liberação. O recálculo da liberação de embarque fica para DEPOIS do commit, como a expedição faz no
/// modo dela — antes do commit ele contaria a cópia de venda.
/// </remarks>
public class ShipmentLoadsShipService(
    IUnitOfWork db,
    ShippingTransactionsCreateService shippingCreate,
    ShipmentLoadsAttachTransactionsService attach,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ILogger<ShipmentLoadsShipService> logger)
{
    public async Task<ShipmentLoadShipResult> ExecuteAsync(ShipmentLoadShipRequest request, string userName)
    {
        if (request.GrossWeight <= decimal.Zero)
            throw new ApplicationException("Informe o peso bruto maior que zero.");

        var load = await db.Context.ShipmentLoads.AsNoTracking()
                       .FirstOrDefaultAsync(x => x.Key == request.ShipmentLoadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {request.ShipmentLoadKey}");

        if (load.LoadType != ShipmentLoadType.Normal ||
            load.Status is not (ShipmentLoadStatus.Planned or ShipmentLoadStatus.Open or ShipmentLoadStatus.InTransshipment))
            throw new ApplicationException($"A carga {load.Code} não aceita expedição nesta situação.");

        if (string.IsNullOrWhiteSpace(load.TruckCode))
            throw new ApplicationException($"A carga {load.Code} não tem placa: informe a placa na carga antes de expedir.");

        var release = await db.Context.ShipmentReleases.AsNoTracking()
                          .Include(x => x.PurchaseContract)
                          .FirstOrDefaultAsync(x => x.Key == request.ShipmentReleaseKey)
                      ?? throw new ApplicationException("Liberação de embarque não encontrada.");

        if (release.PurchaseContract?.ItemCode != load.ItemCode)
            throw new ApplicationException("A liberação escolhida é de outro produto.");

        var purchaseContractKey = ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin)
            ? (Guid?)null
            : release.PurchaseContractKey;

        var purchase = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            BranchCode = load.BranchCode,
            CardCode = release.PurchaseContract.CardCode,
            CardName = release.PurchaseContract.CardName,
            ItemCode = load.ItemCode,
            ItemName = load.ItemName,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = request.WarehouseCode,
            TruckCode = load.TruckCode,
            TruckDriverCode = request.TruckDriverCode,
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionDate = request.TransactionDate,
            GrossWeight = request.GrossWeight,
            ShipmentReleaseKey = release.Key,
            Comments = request.Comments,
        };

        StorageTransaction exit;
        try
        {
            await db.BeginTransactionAsync();

            var shipping = await shippingCreate.ExecuteAsync(purchaseContractKey, purchase, userName, CommitMode.Deferred);
            exit = shipping.SalesStorageTransaction!;

            await attach.ExecuteAsync(load.Key, [exit.Key], transshipmentKey: null, userName, CommitMode.Deferred);

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        // O recálculo é idempotente e a expedição já está commitada: falhar aqui convidaria o operador a expedir de novo.
        try
        {
            await recalcShipped.RecalculateAsync(release.Key);
        }
        catch (Exception e)
        {
            logger.LogWarning(e,
                "Romaneio {Code} expedido na carga {LoadCode}, mas o saldo da liberação {ReleaseKey} não foi recalculado: {Message}",
                exit.Code, load.Code, release.Key, e.Message);
        }

        return new ShipmentLoadShipResult(load.Key, exit.Key, exit.Code);
    }
}
