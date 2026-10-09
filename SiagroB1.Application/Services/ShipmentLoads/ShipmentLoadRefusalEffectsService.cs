using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>O que a recusa precisa para aplicar o destino físico. <c>RefusedInvoices</c> são as notas de ORIGEM.</summary>
public sealed record RefusalEffectsInput(
    ShipmentLoad Load,
    RefusalDestination Destination,
    string? WarehouseCode,
    string? WarehouseName,
    IReadOnlyList<SalesInvoice> RefusedInvoices,
    decimal TotalQuantity,
    string Reason);

/// <summary>
/// Efeitos físicos do destino da recusa — devolução ao armazém ou abertura do transbordo. Usado pela recusa síncrona
/// (<see cref="ShipmentLoadsRefuseService"/>) e pela conclusão da recusa em dois tempos
/// (<c>ShipmentLoadRefusalCompleteService</c>, spec 2026-10-09), para que as três chaves proibidas do romaneio 12 e
/// a regra do transbordo vivam num lugar só.
/// </summary>
/// <remarks>
/// Roda SEMPRE dentro da transação de quem chama: todos os serviços internos em <see cref="CommitMode.Deferred"/>.
/// Não recheca a elegibilidade do transbordo — quem chama decide (a conclusão não pode chamar
/// <c>EnsureLoadAcceptsTransshipment</c>, porque a carga está em <c>RefusalPending</c>).
/// </remarks>
public class ShipmentLoadRefusalEffectsService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageCreate,
    StorageTransactionsConfirmedService storageConfirm,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentReleasesFromReturnService returnReleases)
{
    public async Task ApplyAsync(RefusalEffectsInput input, string userName)
    {
        switch (input.Destination)
        {
            case RefusalDestination.Warehouse:
                await ReturnToWarehouseAsync(input, userName);
                break;
            case RefusalDestination.Transshipment:
                await OpenTransshipmentAsync(input, userName);
                break;
        }
    }

    /// <summary>
    /// Descarrega a mercadoria recusada no armazém escolhido e retira o volume da carga.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Três chaves que este romaneio NÃO pode carregar</b>, cada uma por um motivo próprio:
    /// <list type="bullet">
    /// <item><c>ShipmentLoadKey</c> — <c>ShipmentLoadsRecalculateTotalService</c> soma o
    /// <c>GrossWeight</c> das transações da carga para obter o volume EMBARCADO. A devolução
    /// aumentaria o total da carga de onde a mercadoria saiu. O vínculo certo é
    /// <c>RefusedFromShipmentLoadKey</c>.</item>
    /// <item><c>ShipmentReleaseKey</c> — <c>ShipmentReleasesRecalculateShippedService</c> conta o
    /// tipo 12 no eixo das liberações de COMPRA; a devolução moveria um saldo alheio.</item>
    /// <item><c>ReturnInvoiceKey</c> — é o discriminador <c>isNewFlow</c> de
    /// <c>SalesInvoicesReverseConfirmService</c>: com ela, um estorno carimbaria esta entrada
    /// como <c>Invoiced</c> e a anexaria à nota de origem.</item>
    /// </list>
    /// <c>StorageAddressCode</c> fica nulo porque a recusa é entrada em nível de ARMAZÉM. O
    /// saldo por ENDEREÇO não credita o tipo 12, e a mesma lista de tipos se repete em
    /// <c>StorageAddressesGetBalanceService</c>, <c>StorageAddressesDailyBalanceBuilderService</c>,
    /// <c>StorageAddressesListOpenedByItemService</c>,
    /// <c>StorageAddressesStorageChargeCalculatorService</c>,
    /// <c>StorageAddressesTechnicalLossCalculatorService</c> e
    /// <c>SiagroB1.Reports/Services/StorageAddressReportService</c> — endereçar a devolução exige
    /// acertar os seis de forma consistente. É esse o checklist, se um dia for preciso.
    /// </remarks>
    private async Task ReturnToWarehouseAsync(RefusalEffectsInput input, string userName)
    {
        var load = input.Load;
        var invoiceNumbers = FormatInvoiceNumbers(input.RefusedInvoices);
        var cardCodes = string.Join(", ", input.RefusedInvoices.Select(i => i.CardCode).Distinct());

        var entry = new StorageTransaction
        {
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionDate = DateTime.Now.Date,
            BranchCode = load.BranchCode,
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = input.WarehouseCode!,
            // A coluna é NOT NULL. Recusa de documentos de clientes diferentes numa entrada só
            // grava o primeiro; todos ficam listados no Comments e na narrativa do movimento.
            CardCode = input.RefusedInvoices[0].CardCode,
            TruckCode = load.TruckCode,
            TruckDriverCode = load.TruckDriverCode,
            GrossWeight = input.TotalQuantity,
            NetWeight = input.TotalQuantity,
            RefusedFromShipmentLoadKey = load.Key,
            Comments =
                $"Devolução por recusa da carga {load.Code}. Motivo: {input.Reason}. " +
                $"Documento(s): {invoiceNumbers}. Cliente(s): {cardCodes}.",
        };

        await storageCreate.ExecuteAsync(
            entry, userName, TransactionCode.ShipmentLoad, CommitMode.Deferred);

        await db.SaveChangesAsync();

        await storageConfirm.ExecuteAsync(entry, userName, CommitMode.Deferred);

        // DEPOIS do SaveChanges: o terceiro termo do saldo é um somatório no SERVIDOR e leria o
        // estado anterior se a entrada ainda não estivesse gravada.
        await db.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
            db.Context, load.Key, excludedInvoiceKeys: null);

        await EmitReturnReleasesAsync(
            load, entry, input.WarehouseCode!, input.WarehouseName, input.TotalQuantity, userName);

        movementLog.Register(
            load.Key,
            ShipmentLoadMovementType.ReturnedToWarehouse,
            -input.TotalQuantity,
            load.AvailableQuantity,
            $"Mercadoria devolvida ao armazém ({input.WarehouseCode}) {input.WarehouseName} " +
            $"pelo romaneio {entry.Code}: {input.TotalQuantity:N3}.",
            userName,
            movementContext: new ShipmentLoadMovementContext(
                CardCode: input.RefusedInvoices[0].CardCode,
                CardName: input.RefusedInvoices[0].CardName,
                DeliveryCardCode: input.RefusedInvoices[0].DeliveryCardCode,
                DeliveryCardName: input.RefusedInvoices[0].DeliveryCardName,
                WarehouseCode: input.WarehouseCode,
                WarehouseName: input.WarehouseName,
                Reason: input.Reason,
                StorageTransactionKey: entry.Key));
    }

    /// <summary>
    /// Abre o transbordo da carga (GAC-1181) com o volume recusado nesta chamada. A entrada no
    /// armazém (que credita o saldo dele) e o eventual romaneio/liberação nascem depois, em
    /// <c>ShipmentLoadsTransshipmentRegisterEntryService</c>, quando o caminhão for pesado lá —
    /// aqui a mercadoria ainda está a caminho, só o saldo da carga já sai.
    /// </summary>
    /// <remarks>
    /// <c>Sequence</c> vem de <see cref="ShipmentLoadTransshipmentRules.NextSequenceAsync"/>, o
    /// mesmo método que <c>ShipmentLoadsTransshipmentStartService</c> usa — nunca "último + 1"
    /// duplicado aqui. <c>OutgoingQuantity</c> é o total RECUSADO nesta chamada (não o saldo
    /// disponível inteiro da carga, ao contrário do início "planejado"): uma recusa parcial só
    /// transborda a parte recusada, o resto segue com o rótulo que já tinha.
    /// <para>
    /// <b>A trava de "não empilha transbordo aberto"
    /// (<see cref="ShipmentLoadTransshipmentRules.EnsureIsLastAsync"/>) roda em quem chama</b>
    /// (<see cref="ShipmentLoadsRefuseService"/>), antes de qualquer devolução — alcançável por aqui pela
    /// recusa PARCIAL repetida: recusar 10 t para Transbordo duas vezes seguidas, sem registrar a
    /// entrada nem vincular a saída do primeiro, abriria um segundo transbordo que
    /// <c>HasOpenTransshipmentAsync</c> não enxergaria (ele só olha o ÚLTIMO por
    /// <c>Sequence</c>), deixando o primeiro aberto para sempre, em silêncio.
    /// </para>
    /// </remarks>
    private async Task OpenTransshipmentAsync(RefusalEffectsInput input, string userName)
    {
        var load = input.Load;
        var sequence = await ShipmentLoadTransshipmentRules.NextSequenceAsync(db.Context, load.Key);

        var invoiceNumbers = FormatInvoiceNumbers(input.RefusedInvoices);

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = sequence,
            Origin = TransshipmentOrigin.Refusal,
            WarehouseCode = input.WarehouseCode!,
            WarehouseName = input.WarehouseName,
            TransshipmentDate = DateTime.Today,
            OutgoingQuantity = input.TotalQuantity,
            // Narrativa própria do transbordo (não só a do movimento): nomeia o(s) documento(s)
            // recusado(s), mesmo espírito do Comments que a recusa já escreve no romaneio do
            // destino Warehouse (ver ReturnToWarehouseAsync).
            Comments = ShipmentLoadTransshipmentRules.Truncate(
                $"Transbordo aberto pela recusa da carga {load.Code}. Documento(s) " +
                $"recusado(s): {invoiceNumbers}."),
            CreatedBy = userName,
            UpdatedBy = userName,
        };

        db.Context.ShipmentLoadsTransshipments.Add(transshipment);

        // O quarto termo do saldo é um somatório no SERVIDOR — a linha precisa estar gravada
        // antes do recálculo, senão ele lê o estado anterior.
        await db.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
            db.Context, load.Key, excludedInvoiceKeys: null);

        movementLog.Register(
            load.Key,
            ShipmentLoadMovementType.TransshipmentStarted,
            -input.TotalQuantity,
            load.AvailableQuantity,
            $"Mercadoria recusada enviada para transbordo no armazém " +
            $"({transshipment.WarehouseCode}) {transshipment.WarehouseName}: {input.TotalQuantity:N3}.",
            userName,
            movementContext: new ShipmentLoadMovementContext(
                WarehouseCode: transshipment.WarehouseCode,
                WarehouseName: transshipment.WarehouseName));
    }

    /// <summary>
    /// Emite as liberações que devolvem a mercadoria recusada à Expedição de Grãos.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>O rateio é por PESO dos romaneios da carga, e não pelos documentos recusados.</b> A
    /// escolha do operador é por documento (<see cref="RefusalLine"/>), mas a nota de carga nasce
    /// com <c>SalesTransactions</c> vazia — o faturamento consome o saldo da carga por
    /// QUANTIDADE, sem vincular romaneio a nota. Logo não existe dado que diga quais kg
    /// devolvidos vieram de qual contrato, e a única fonte de contrato é
    /// <c>load.Transactions</c>. O pro-rata é a atribuição menos arbitrária disponível, não um
    /// cálculo exato — ver <c>DistributeByWeight</c>.
    /// <para>
    /// Falha aqui <b>não</b> pode derrubar a recusa: o caminhão já descarregou. Volume sem
    /// contrato rastreável fica sem liberação e o motivo vai para o <c>Comments</c> da entrada.
    /// </para>
    /// </remarks>
    private async Task EmitReturnReleasesAsync(
        ShipmentLoad load,
        StorageTransaction entry,
        string warehouseCode,
        string? warehouseName,
        decimal totalQuantity,
        string userName)
    {
        var shipments = await db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == load.Key &&
                        x.TransactionType == StorageTransactionType.SalesShipment &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .ToListAsync();

        if (shipments.Count == 0)
            return;

        var shares = ShipmentReleasesFromReturnService.DistributeByWeight(shipments, totalQuantity);

        var build = await returnReleases.BuildAsync(
            entry, shares, warehouseCode, warehouseName, userName, ReleaseOrigin.SalesReturn);

        if (build.Releases.Count > 0)
            db.Context.ShipmentReleases.AddRange(build.Releases);

        if (build.Note is not null)
            entry.Comments = AppendComment(entry.Comments, build.Note);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Concatena respeitando o VARCHAR(500) da coluna — o texto base já é longo, e estourar aqui
    /// derrubaria a recusa inteira num SaveChanges.
    /// </summary>
    private static string AppendComment(string? current, string addition)
    {
        var merged = string.IsNullOrWhiteSpace(current) ? addition : $"{current} {addition}";
        return merged.Length <= 500 ? merged : merged[..500];
    }

    /// <summary>
    /// Lista os documentos recusados para a narrativa do romaneio (<see cref="ReturnToWarehouseAsync"/>)
    /// e do transbordo (<see cref="OpenTransshipmentAsync"/>) — os dois textam o mesmo conjunto de
    /// notas, cada um no seu Comments.
    /// </summary>
    private static string FormatInvoiceNumbers(IReadOnlyList<SalesInvoice> invoices) =>
        string.Join(", ", invoices.Select(i => i.InvoiceNumber));
}
