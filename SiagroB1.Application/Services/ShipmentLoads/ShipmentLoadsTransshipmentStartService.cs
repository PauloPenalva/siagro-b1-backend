using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Inicia o transbordo da carga (GAC-1181): descarrega o saldo INTEIRO disponível num armazém
/// intermediário.
/// </summary>
/// <remarks>
/// Primeiro dos quatro serviços de escrita do módulo — registrar a entrada é a Task 5, estornar é
/// a Task 6, a recusa com destino Transbordo é a Task 8.
/// <para>
/// <see cref="ShipmentLoadTransshipment.OutgoingQuantity"/> é o saldo RECALCULADO no momento, não
/// o persistido: sob drift, ler o persistido barraria ou liberaria o transbordo errado — mesmo
/// precedente de <see cref="ShipmentLoadsBillingGuardService"/>.
/// </para>
/// <para>
/// O armazém é resolvido por <see cref="IWarehouseService"/>, nunca pela tabela local: em modo
/// SAPB1 ela fica vazia e o armazém é parceiro de negócio. <c>WarehouseCode</c>/<c>WarehouseName</c>
/// são gravados desnormalizados, como o resto da carga já faz.
/// </para>
/// </remarks>
public class ShipmentLoadsTransshipmentStartService(
    IUnitOfWork db,
    IWarehouseService warehouseService,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoadTransshipment> ExecuteAsync(
        Guid loadKey,
        string warehouseCode,
        DateTime transshipmentDate,
        string? comments,
        string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == loadKey) ??
                   throw new NotFoundException($"Shipment load not found key {loadKey}");

        // TODA a validação antes de qualquer escrita: um início recusado não pode deixar efeito
        // no banco.
        ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(load);
        ShipmentLoadTransshipmentRules.EnsureWarehouseInformed(warehouseCode);

        var warehouse = await warehouseService.GetByIdAsync(warehouseCode.Trim())
                        ?? throw new ApplicationException($"Armazém {warehouseCode} não encontrado.");

        var invoiced = await ShipmentLoadsRecalculateInvoicedService.CalculateInvoicedAsync(
            db.Context, loadKey, excludedInvoiceKeys: null);
        var returned = await ShipmentLoadsRecalculateReturnedService
            .CalculateReturnedToWarehouseAsync(db.Context, loadKey);
        var transshipped = await ShipmentLoadsRecalculateTransshippedService
            .CalculateTransshippedAsync(db.Context, loadKey);

        var available = ShipmentLoad.CalculateAvailableQuantity(
            load.TotalQuantity, invoiced, returned, transshipped);

        if (available <= ShipmentLoadTransshipmentRules.Tolerance)
            throw new ApplicationException(
                $"A carga {load.Code} não tem saldo disponível para transbordo.");

        await ShipmentLoadTransshipmentRules.EnsureIsLastAsync(db.Context, load);

        var sequence = await ShipmentLoadTransshipmentRules.NextSequenceAsync(db.Context, loadKey);

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = loadKey,
            Sequence = sequence,
            Origin = TransshipmentOrigin.Planned,
            WarehouseCode = warehouse.Code ?? warehouseCode.Trim(),
            WarehouseName = warehouse.Name,
            TransshipmentDate = transshipmentDate.Date,
            OutgoingQuantity = available,
            EntryQuantity = decimal.Zero,
            Comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim(),
            CreatedBy = userName,
            UpdatedBy = userName,
        };

        try
        {
            await db.BeginTransactionAsync();

            db.Context.ShipmentLoadsTransshipments.Add(transshipment);

            // Recalcular só DEPOIS do SaveChanges que gravou a FK — o recálculo consulta o banco
            // e não enxerga o change tracker.
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, loadKey, excludedInvoiceKeys: null);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransshipmentStarted,
                -available,
                load.AvailableQuantity,
                $"Transbordo {sequence} iniciado no armazém ({transshipment.WarehouseCode}) " +
                $"{transshipment.WarehouseName}: {available:N3}.",
                userName,
                movementContext: new ShipmentLoadMovementContext(
                    WarehouseCode: transshipment.WarehouseCode,
                    WarehouseName: transshipment.WarehouseName));

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return transshipment;
    }
}
