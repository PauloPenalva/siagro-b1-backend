using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Registra a entrada do transbordo da carga (GAC-1181): a mercadoria descarregada no armazém
/// intermediário chega ao lote (armazém próprio) ou vira o romaneio
/// <see cref="StorageTransactionType.TransshipmentReceipt"/> (armazém de terceiro), que emite as
/// liberações que devolvem o volume à Expedição de Grãos.
/// </summary>
/// <remarks>
/// Segundo dos quatro serviços de escrita do módulo — iniciar é a Task 4, estornar é a Task 6, a
/// recusa com destino Transbordo é a Task 8.
/// <para>
/// <b>Tudo numa transação só, e por isso todos os serviços internos são chamados em
/// <see cref="CommitMode.Deferred"/>.</b> <c>UnitOfWork.CommitAsync</c> comita e zera a transação
/// INCONDICIONALMENTE: um único serviço interno em <c>Auto</c> comitaria a transação daqui no meio
/// da operação, e o resto rodaria desprotegido. Mesmo precedente de
/// <see cref="ShipmentLoadsRefuseService"/>.
/// </para>
/// <para>
/// ⚠️ <b>O romaneio 15 NÃO carrega <see cref="StorageTransaction.ShipmentReleaseKey"/> nem
/// <see cref="StorageTransaction.ShipmentLoadKey"/>.</b> Com a primeira, o romaneio que ORIGINA a
/// liberação passaria a CONSUMI-la — o vínculo certo é o inverso,
/// <see cref="ShipmentRelease.GeneratedByStorageTransactionKey"/> apontando o 15. Com a segunda, o
/// cancelamento da carga (que zera <c>ShipmentLoadKey</c> em todos os romaneios dela) deixaria a
/// entrada órfã; a carga o alcança por <see cref="StorageTransaction.ShipmentLoadTransshipmentKey"/>.
/// </para>
/// <para>
/// <b>Não existe <c>ShipmentReleasesFromTransshipmentService</c>.</b> O rastreio do contrato
/// (cadeia curta pela <c>ShipmentReleaseKey</c>, cadeia longa por <c>SHIPPING_TRANSACTIONS</c>), o
/// rateio por peso e o tratamento de volume órfão são idênticos aos da devolução ao armazém —
/// <see cref="ShipmentReleasesFromReturnService"/> recebe a origem como parâmetro obrigatório em
/// vez de duplicar a regra.
/// </para>
/// </remarks>
public class ShipmentLoadsTransshipmentRegisterEntryService(
    IUnitOfWork db,
    IWarehouseService warehouseService,
    IWarehouseComplementService warehouseComplementService,
    StorageTransactionsCreateService storageCreate,
    StorageTransactionsConfirmedService storageConfirm,
    ShipmentReleasesFromReturnService returnReleases,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoadTransshipment> ExecuteAsync(
        Guid transshipmentKey,
        decimal grossWeight,
        DateTime entryDate,
        Guid? receiptStorageTransactionKey,
        string userName)
    {
        var transshipment = await db.Context.ShipmentLoadsTransshipments
                                 .FirstOrDefaultAsync(x => x.Key == transshipmentKey) ??
                             throw new NotFoundException(
                                 $"Shipment load transshipment not found key {transshipmentKey}");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == transshipment.ShipmentLoadKey) ??
                   throw new NotFoundException(
                       $"Shipment load not found key {transshipment.ShipmentLoadKey}");

        // TODA a validação antes de qualquer escrita: um registro recusado não pode deixar efeito
        // no banco.
        ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(load);
        ShipmentLoadTransshipmentRules.EnsureEntryNotAlreadyRegistered(transshipment);

        var warehouse = await warehouseService.GetByIdAsync(transshipment.WarehouseCode)
                        ?? throw new ApplicationException(
                            $"Armazém {transshipment.WarehouseCode} não encontrado.");

        var complement = await warehouseComplementService.GetAsync(transshipment.WarehouseCode);
        var isOwn = complement?.IsOwn == true;

        StorageTransaction? existingReceipt = null;
        decimal quantity;

        if (isOwn)
        {
            if (receiptStorageTransactionKey is not { } receiptKey)
                throw new ApplicationException(
                    "Informe o romaneio de Entrada em Armazenagem que recebeu o transbordo.");

            existingReceipt = await db.Context.StorageTransactions
                                   .FirstOrDefaultAsync(x => x.Key == receiptKey) ??
                               throw new NotFoundException(
                                   $"Storage transaction not found key {receiptKey}");

            ShipmentLoadTransshipmentRules.EnsureOwnWarehouseReceiptIsUsable(
                existingReceipt, load, transshipment);

            quantity = existingReceipt.GrossWeight;
        }
        else
        {
            // Peso único da entrada: o que foi pesado ao descarregar no armazém intermediário.
            // Não existe tara em StorageTransaction (só GrossWeight/NetWeight) — a TareWeight que
            // existe é a do CAMINHÃO, outra coisa. A quebra de transporte já é a diferença entre
            // o que saiu da carga (OutgoingQuantity) e este peso.
            quantity = grossWeight;

            ShipmentLoadTransshipmentRules.EnsureThirdPartyQuantityIsValid(quantity, transshipment);
        }

        // Romaneios de saída da ORIGEM da carga (não os de um transbordo anterior): é por eles
        // que o contrato de compra é rastreado, e é entre eles que o peso de entrada é rateado —
        // a mesma leitura da recusa (ShipmentLoadsRefuseService.EmitReturnReleasesAsync).
        var originShipments = await db.Context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == load.Key &&
                        x.TransactionType == StorageTransactionType.SalesShipment &&
                        x.ShipmentLoadTransshipmentKey == null &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .OrderBy(x => x.RowId)
            .ToListAsync();

        if (!isOwn && originShipments.Count == 0)
            throw new ApplicationException(
                $"A carga {load.Code} não tem romaneio de saída de origem.");

        try
        {
            await db.BeginTransactionAsync();

            StorageTransaction entry;

            if (isOwn)
            {
                // Não nasce romaneio novo: o grão já entrou no lote pela Entrada em Armazenagem
                // lançada na tela de sempre — aqui só se registra o papel dela no transbordo.
                existingReceipt!.ShipmentLoadTransshipmentKey = transshipment.Key;
                entry = existingReceipt;
            }
            else
            {
                entry = await CreateThirdPartyReceiptAsync(
                    load, transshipment, warehouse, originShipments, quantity, entryDate, userName);

                // Volume sem contrato rastreável não derruba o registro porque
                // ShipmentReleasesFromReturnService.BuildAsync não lança nesse caso — degrada
                // para Note (que vira Comments do 15). Não é isolamento de falha: uma exceção
                // REAL aqui rodaria no mesmo try/catch que faz RollbackAsync logo abaixo e
                // derrubaria a transação inteira, romaneio 15 incluído.
                await EmitReleasesAsync(
                    entry, originShipments, warehouse, quantity, userName);
            }

            transshipment.EntryQuantity = quantity;
            transshipment.EntryStorageTransactionKey = entry.Key;
            transshipment.UpdatedAt = DateTime.Now;
            transshipment.UpdatedBy = userName;

            // Recalcular só DEPOIS do SaveChanges que gravou as FKs — o recálculo consulta o
            // banco e não enxerga o change tracker.
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransshipmentEntered,
                decimal.Zero,
                load.AvailableQuantity,
                $"Entrada do transbordo {transshipment.Sequence} registrada no armazém " +
                $"({transshipment.WarehouseCode}) {transshipment.WarehouseName}: {quantity:N3}. " +
                $"Quebra: {transshipment.ShrinkageQuantity:N3}. Romaneio {entry.Code}.",
                userName,
                movementContext: new ShipmentLoadMovementContext(
                    WarehouseCode: transshipment.WarehouseCode,
                    WarehouseName: transshipment.WarehouseName,
                    StorageTransactionKey: entry.Key));

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

    /// <summary>
    /// Monta e confirma o romaneio 15 em armazém de terceiro. <c>NetWeight = GrossWeight</c> é
    /// aplicado pela confirmação (<c>StorageTransactionsConfirmedService</c>, Task 2) — não há
    /// tabela de custos no transbordo.
    /// </summary>
    private async Task<StorageTransaction> CreateThirdPartyReceiptAsync(
        ShipmentLoad load,
        ShipmentLoadTransshipment transshipment,
        WarehouseModel warehouse,
        IReadOnlyList<StorageTransaction> originShipments,
        decimal quantity,
        DateTime entryDate,
        string userName)
    {
        var codes = string.Join(", ", originShipments
            .Select(x => x.Code)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct());

        var cardCodes = originShipments.Select(x => x.CardCode).Distinct().ToList();

        var comments =
            $"Entrada do transbordo {transshipment.Sequence} da carga {load.Code}. " +
            $"Romaneio(s) de saída da origem: {codes}." +
            (cardCodes.Count > 1
                ? $" Cliente(s)/Fornecedor(s): {string.Join(", ", cardCodes)}."
                : string.Empty);

        var entry = new StorageTransaction
        {
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionDate = entryDate.Date,
            BranchCode = load.BranchCode,
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = warehouse.Code ?? transshipment.WarehouseCode,
            // A coluna é NOT NULL. O 15 não é documento de ninguém em particular — usa o
            // CardCode de quem já está na carga, o primeiro romaneio de saída da origem.
            CardCode = cardCodes[0],
            TruckCode = load.TruckCode,
            TruckDriverCode = load.TruckDriverCode,
            GrossWeight = quantity,
            NetWeight = quantity,
            ShipmentLoadTransshipmentKey = transshipment.Key,
            Comments = ShipmentLoadTransshipmentRules.Truncate(comments),
        };

        await storageCreate.ExecuteAsync(
            entry, userName, TransactionCode.ShipmentLoad, CommitMode.Deferred);

        await db.SaveChangesAsync();

        await storageConfirm.ExecuteAsync(entry, userName, CommitMode.Deferred);

        await db.SaveChangesAsync();

        return entry;
    }

    /// <summary>
    /// Emite as liberações que devolvem a mercadoria transbordada à Expedição de Grãos, uma por
    /// contrato de compra rastreado nos romaneios de saída da origem.
    /// </summary>
    private async Task EmitReleasesAsync(
        StorageTransaction entry,
        IReadOnlyList<StorageTransaction> originShipments,
        WarehouseModel warehouse,
        decimal quantity,
        string userName)
    {
        var shares = ShipmentReleasesFromReturnService.DistributeByWeight(originShipments, quantity);

        var build = await returnReleases.BuildAsync(
            entry, shares, warehouse.Code ?? entry.WarehouseCode, warehouse.Name, userName,
            ReleaseOrigin.Transshipment);

        if (build.Releases.Count > 0)
            db.Context.ShipmentReleases.AddRange(build.Releases);

        if (build.Note is not null)
            entry.Comments = AppendComment(entry.Comments, build.Note);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Concatena respeitando o VARCHAR(500) da coluna — mesmo precedente de
    /// <see cref="ShipmentLoadsRefuseService"/>.
    /// </summary>
    private static string AppendComment(string? current, string addition)
    {
        var merged = string.IsNullOrWhiteSpace(current) ? addition : $"{current} {addition}";
        return ShipmentLoadTransshipmentRules.Truncate(merged);
    }
}
