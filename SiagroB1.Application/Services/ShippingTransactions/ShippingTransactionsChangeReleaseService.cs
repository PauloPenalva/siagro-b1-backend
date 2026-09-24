using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShippingTransactions;

public sealed record ShippingReleaseChangeItem(Guid SalesStorageTransactionKey, Guid TargetShipmentReleaseKey);

/// <summary>
/// Troca a liberação de embarque — e com ela contrato, fornecedor e armazém — de Expedições que
/// estão numa carga, inclusive faturada (GAC-1177, v2). O caso: o armazém informa depois qual
/// contrato baixou, o produtor diz que a baixa foi do lote da Yokotobi, ou o financeiro pede outro
/// contrato primeiro.
/// </summary>
/// <remarks>
/// <para>
/// <b>A troca é explícita, por romaneios de compensação</b>, todos com a data da carga e amarrados
/// por um <see cref="ShippingReleaseChange"/>:
/// </para>
/// <list type="bullet">
/// <item><b>Original</b> (7 e, se houver, 8): intacta, sai da carga e fica marcada
/// <see cref="StorageTransaction.ReplacedByShippingReleaseChangeKey"/>.</item>
/// <item><b>Estorno na origem</b>: 12 com o bruto original (devolve armazém/lote e, sem perna de
/// compra, a liberação) e, quando a original tem perna 8, um 9 com os mesmos pesos e descontos
/// dela e alocação NEGATIVA no contrato de origem.</item>
/// <item><b>Expedição nova no destino</b>: 7 na carga (armazém/lote da liberação de destino,
/// status e nota da original) e, quando o destino tem perna de compra, 8 confirmado e alocado.</item>
/// </list>
/// <para>
/// <b>Lote:</b> vários itens numa chamada é o que permite INVERTER Expedições. Tudo é validado
/// antes de escrever; todos os estornos são gravados antes de qualquer Expedição nova (o contrato e
/// o lote de um item podem depender do que o estorno de outro devolve); o saldo das liberações é
/// conferido só no fim, depois do recálculo.
/// </para>
/// <para>
/// <b>Ciclo de FKs:</b> <see cref="ShippingReleaseChange"/> aponta os romaneios gerados e eles
/// apontam de volta o documento. Inseridos no mesmo <c>SaveChanges</c>, o SQL Server recusaria
/// (EF: dependência circular). Por isso os romaneios são gravados primeiro sem a chave da troca, e
/// o documento entra num segundo <c>SaveChanges</c>, junto com os carimbos nos romaneios.
/// </para>
/// </remarks>
public class ShippingTransactionsChangeReleaseService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageCreateService,
    StorageTransactionsConfirmedService storageConfirmedService,
    PurchaseContractsAllocationCreateService allocationCreateService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ShipmentLoadsMovementLogService movementLog,
    ShippingReleaseChangeReconciliationGuard reconciliationGuard,
    IStorageAddressBalanceReader balanceReader)
{
    private const decimal Tolerance = 0.001m;

    private sealed class Plan
    {
        public required ShippingTransaction Shipping { get; init; }
        public required StorageTransaction Sales { get; init; }
        public StorageTransaction? Purchase { get; init; }
        public required ShipmentLoad Load { get; init; }
        public required ShipmentRelease Source { get; init; }
        public required ShipmentRelease Target { get; init; }
        public decimal NewQuantity { get; set; }
        public StorageAddress? TargetLot { get; set; }

        public StorageTransaction? ReturnSales { get; set; }
        public StorageTransaction? ReturnPurchase { get; set; }
        public StorageTransaction? NewSales { get; set; }
        public StorageTransaction? NewPurchase { get; set; }
    }

    public async Task ExecuteAsync(IReadOnlyList<ShippingReleaseChangeItem> items, string? reason, string userName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo da troca de liberação.");

        if (items.Count == 0)
            throw new ApplicationException("Selecione ao menos um romaneio para trocar a liberação.");

        if (items.Select(x => x.SalesStorageTransactionKey).Distinct().Count() != items.Count)
            throw new ApplicationException("O mesmo romaneio foi informado mais de uma vez.");

        reason = reason.Trim();

        // ── Validação completa antes de qualquer escrita ──
        var plans = new List<Plan>();
        foreach (var item in items)
            plans.Add(await BuildPlanAsync(item));

        foreach (var plan in plans)
            plan.NewQuantity = await ResolveNewQuantityAsync(plan, items.Count);

        foreach (var plan in plans)
            await reconciliationGuard.EnsureNoApprovedReconciliationAsync(
                plan.Sales.ItemCode,
                [plan.Sales.WarehouseCode, plan.Target.DeliveryLocationCode],
                plan.Load.LoadDate);

        await ResolveLotsAsync(plans);

        var operationGroupKey = Guid.NewGuid();

        try
        {
            await db.BeginTransactionAsync();

            // Fase 1 — estornos na origem de TODOS os itens (devolve contrato, liberação e lote).
            foreach (var plan in plans)
                await ReverseAtOriginAsync(plan, userName);

            // Fase 2 — Expedições novas no destino.
            foreach (var plan in plans)
                await CreateAtDestinationAsync(plan, userName);

            await db.SaveChangesAsync();

            // Fase 3 — documento da troca + carimbos (segundo SaveChanges: ver <remarks>).
            foreach (var plan in plans)
                RegisterChange(plan, operationGroupKey, reason, userName);

            await db.SaveChangesAsync();

            // Fase 4 — recálculo de TODAS as liberações tocadas: o fluxo inteiro roda em
            // Deferred, que não dispara os hooks de ShippedQuantity.
            var releaseKeys = plans.SelectMany(p => new[] { p.Source.Key, p.Target.Key }).Distinct();
            foreach (var releaseKey in releaseKeys)
                await recalcShipped.RecalculateAsync(releaseKey);

            // Fase 5 — saldo das liberações de destino, só agora que o lote inteiro foi aplicado.
            foreach (var target in plans.Select(p => p.Target).DistinctBy(x => x.Key))
            {
                var balance = target.ReleasedQuantity - target.ShippedQuantity;
                if (balance < -Tolerance)
                    throw new ApplicationException(
                        $"Saldo insuficiente na liberação do contrato {target.PurchaseContract!.Code}: " +
                        $"faltam {-balance:N3}.");
            }

            // Fase 6 — carga: total (só vigentes) e faturado/status, pelos escritores únicos.
            foreach (var load in plans.Select(p => p.Load).DistinctBy(x => x.Key))
            {
                await ShipmentLoadsRecalculateTotalService.RecalculateAsync(db.Context, load.Key);
                await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                    db.Context, load.Key, excludedInvoiceKeys: null);
            }

            // Fase 7 — narrativa na carga.
            foreach (var plan in plans)
                RegisterMovement(plan, reason, userName);

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    // ───────────────────────────── Validação ─────────────────────────────

    private async Task<Plan> BuildPlanAsync(ShippingReleaseChangeItem item)
    {
        var shipping = await db.Context.ShippingTransactions
                           .Include(x => x.PurchaseStorageTransaction)
                           .Include(x => x.SalesStorageTransaction)
                           .FirstOrDefaultAsync(x => x.SalesStorageTransactionKey == item.SalesStorageTransactionKey)
                       ?? throw new NotFoundException("Expedição não encontrada para o romaneio informado.");

        var sales = shipping.SalesStorageTransaction!;

        if (sales.ReplacedByShippingReleaseChangeKey != null)
            throw new ApplicationException(
                $"O romaneio {sales.Code} já foi substituído por uma troca de liberação: troque a Expedição vigente.");

        if (sales.TransactionType != StorageTransactionType.SalesShipment)
            throw new ApplicationException($"O romaneio {sales.Code} não é a perna de saída de uma Expedição.");

        // GAC-1181: a entrada do transbordo já rateou suas liberações por peso entre os
        // romaneios de saída da ORIGEM — trocar a liberação da saída do transbordo por aqui
        // romperia esse rateio sem que o estorno do transbordo saiba.
        if (sales.ShipmentLoadTransshipmentKey != null)
            throw new ApplicationException(
                "A troca de liberação não está disponível para a saída de um transbordo.");

        if (sales.TransactionStatus is StorageTransactionsStatus.Cancelled or StorageTransactionsStatus.Returned)
            throw new ApplicationException(
                $"O romaneio {sales.Code} está cancelado ou devolvido e não pode ter a liberação trocada.");

        if (sales.ShipmentLoadKey is null)
            throw new ApplicationException($"O romaneio {sales.Code} não está em nenhuma carga.");

        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == sales.ShipmentLoadKey);

        // Completed (Remoção, ou Normal com a conferência encerrada) e Discharged (GAC-1171): a
        // composição de uma carga entregue não muda.
        if (load.Status is ShipmentLoadStatus.Cancelled or ShipmentLoadStatus.Returned
            or ShipmentLoadStatus.Completed or ShipmentLoadStatus.Discharged)
        {
            throw new ApplicationException(
                $"A carga {load.Code} está encerrada: a liberação dos romaneios não pode ser trocada.");
        }

        if (sales.ShipmentReleaseKey is null)
            throw new ApplicationException($"O romaneio {sales.Code} não tem liberação de embarque.");

        if (sales.ShipmentReleaseKey == item.TargetShipmentReleaseKey)
            throw new ApplicationException($"O romaneio {sales.Code} já está na liberação escolhida.");

        var source = await LoadReleaseAsync(sales.ShipmentReleaseKey.Value);
        var target = await LoadReleaseAsync(item.TargetShipmentReleaseKey);
        var targetContract = target.PurchaseContract!;

        if (!string.Equals(targetContract.ItemCode, sales.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"A liberação do contrato {targetContract.Code} é de outro produto ({targetContract.ItemCode}).");

        if (target.Status != ReleaseStatus.Actived)
            throw new ApplicationException($"A liberação do contrato {targetContract.Code} não está ativa.");

        // Liberação que não consome contrato (devolução) não é barrada por contrato encerrado.
        foreach (var release in new[] { source, target })
        {
            var contract = release.PurchaseContract!;
            if (ReleaseOriginRules.ConsumesPurchaseContract(release.Origin) &&
                contract.Status == ContractStatus.Finished)
            {
                throw new ApplicationException(
                    $"O contrato {contract.Code} está encerrado: não é possível trocar a liberação.");
            }
        }

        // Inspeções com o atributo: o confirm do 9/8 calcula descontos por QualityAttrib.Type.
        await LoadInspectionsAsync(sales);
        if (shipping.PurchaseStorageTransaction != null)
            await LoadInspectionsAsync(shipping.PurchaseStorageTransaction);

        return new Plan
        {
            Shipping = shipping,
            Sales = sales,
            Purchase = shipping.PurchaseStorageTransaction,
            Load = load,
            Source = source,
            Target = target,
        };
    }

    private async Task LoadInspectionsAsync(StorageTransaction st) =>
        await db.Context.Entry(st).Collection(x => x.QualityInspections)
            .Query()
            .Include(q => q.QualityAttrib)
            .LoadAsync();

    private async Task<ShipmentRelease> LoadReleaseAsync(Guid key) =>
        await db.Context.ShipmentReleases
            .Include(x => x.PurchaseContract)
            .FirstOrDefaultAsync(x => x.Key == key)
        ?? throw new NotFoundException("Liberação de embarque não encontrada.");

    /// <summary>
    /// Quantidade da Expedição nova. Troca de UMA Expedição numa carga com uma só vigente: o
    /// faturado líquido da carga, recalculado das notas (não a coluna persistida). Nos demais
    /// casos (várias vigentes, inversão): o bruto da original.
    /// </summary>
    private async Task<decimal> ResolveNewQuantityAsync(Plan plan, int itemCount)
    {
        if (itemCount != 1)
            return plan.Sales.GrossWeight;

        var currentShipments = await db.Context.StorageTransactions
            .CountAsync(x => x.ShipmentLoadKey == plan.Load.Key &&
                             x.TransactionType == StorageTransactionType.SalesShipment &&
                             x.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                             x.TransactionStatus != StorageTransactionsStatus.Returned);

        if (currentShipments != 1)
            return plan.Sales.GrossWeight;

        var invoiced = await ShipmentLoadsRecalculateInvoicedService.CalculateInvoicedAsync(
            db.Context, plan.Load.Key, excludedInvoiceKeys: null);

        if (invoiced <= decimal.Zero)
            throw new ApplicationException("Carga sem faturamento: desvincule e estorne a Expedição.");

        return invoiced;
    }

    /// <summary>
    /// Lote de destino de cada item. O volume exigido de um lote desconta o bruto das originais
    /// que estão SAINDO dele nesta mesma troca: o estorno 12 devolve esse volume ao lote, mas o
    /// saldo que o leitor enxerga ainda o debita.
    /// </summary>
    private async Task ResolveLotsAsync(List<Plan> plans)
    {
        foreach (var plan in plans)
        {
            var lotCode = plan.Target.StorageAddressCode;

            var required = lotCode == null
                ? plan.NewQuantity
                : plans.Where(p => p.Target.StorageAddressCode == lotCode).Sum(p => p.NewQuantity)
                  - plans.Where(p => p.Sales.StorageAddressCode == lotCode).Sum(p => p.Sales.GrossWeight);

            plan.TargetLot = await ShipmentReleaseLotRules.ResolveAsync(
                db.Context, balanceReader, plan.Target, plan.Sales.ItemCode,
                plan.Target.DeliveryLocationCode, required);
        }
    }

    // ───────────────────────────── Estorno na origem ─────────────────────────────

    private async Task ReverseAtOriginAsync(Plan plan, string userName)
    {
        var original = plan.Sales;

        // a. Estorno 12 — devolve armazém (e lote) com o bruto original.
        var returnSales = StorageTransactionCopyFactory.CreateFrom(original, userName);
        returnSales.QualityInspections.Clear();
        returnSales.TransactionType = StorageTransactionType.SalesShipmentReturn;
        returnSales.TransactionStatus = StorageTransactionsStatus.Pending;
        StampLoadDate(returnSales, plan);
        SetWeights(returnSales, original.GrossWeight);
        returnSales.WarehouseCode = original.WarehouseCode;
        returnSales.WarehouseName = original.WarehouseName;
        returnSales.StorageAddressCode = original.StorageAddressCode;
        ClearDocumentLinks(returnSales);
        // Sem liberação no create/confirm: a de origem costuma estar Finalizada e a trava de
        // movimentação recusaria o estorno.
        returnSales.ShipmentReleaseKey = null;

        await storageCreateService.ExecuteAsync(
            returnSales, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
        await storageConfirmedService.ExecuteAsync(returnSales, userName, CommitMode.Deferred);
        KeepNames(returnSales, original.CardName, original.WarehouseName);

        // Sem perna de compra (transferência/devolução) o eixo da liberação é 7−12: o 12 devolve
        // o romaneado. Na Standard o eixo é 8−9 e quem devolve é o 9.
        if (ReleaseOriginRules.ShipsWithoutPurchaseLeg(plan.Source.Origin))
            returnSales.ShipmentReleaseKey = plan.Source.Key;

        plan.ReturnSales = returnSales;

        // b. Estorno 9 — só quando a original tem perna de compra.
        if (plan.Purchase != null)
            await ReversePurchaseLegAsync(plan, userName);

        // c. A original sai da carga (o carimbo ReplacedBy… entra com o documento, na fase 3).
        original.ShipmentLoadKey = null;
        Touch(original, userName);
    }

    private async Task ReversePurchaseLegAsync(Plan plan, string userName)
    {
        var original = plan.Purchase!;

        var returnPurchase = StorageTransactionCopyFactory.CreateFrom(original, userName);
        returnPurchase.TransactionType = StorageTransactionType.PurchaseReturn;
        returnPurchase.TransactionStatus = StorageTransactionsStatus.Pending;
        StampLoadDate(returnPurchase, plan);
        returnPurchase.StorageAddressCode = null;
        ClearDocumentLinks(returnPurchase);
        returnPurchase.ShipmentReleaseKey = null;

        await storageCreateService.ExecuteAsync(
            returnPurchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
        // Tipo 9 cai no ramo de compra do confirm (CalculateReceipt pela tabela/inspeções).
        await storageConfirmedService.ExecuteAsync(returnPurchase, userName, CommitMode.Deferred);
        KeepNames(returnPurchase, original.CardName, original.WarehouseName);

        // O estorno ESPELHA a 8 original: a tabela de custo pode ter mudado desde a confirmação
        // dela, e qualquer diferença deixaria contrato e liberação com resíduo. Serviço não se
        // estorna por aqui — as tarifas ficam zeradas.
        returnPurchase.GrossWeight = original.GrossWeight;
        returnPurchase.DryingDiscount = original.DryingDiscount;
        returnPurchase.CleaningDiscount = original.CleaningDiscount;
        returnPurchase.OthersDicount = original.OthersDicount;
        returnPurchase.NetWeight = original.NetWeight;
        returnPurchase.AvaiableVolumeToAllocate = original.NetWeight;
        ZeroServicePrices(returnPurchase);

        returnPurchase.ShipmentReleaseKey = plan.Source.Key;
        plan.ReturnPurchase = returnPurchase;

        // Alocação soma pelo BANCO: tudo que está pendente precisa estar gravado antes.
        await db.SaveChangesAsync();

        var contractKey = await ResolveOriginContractKeyAsync(plan);
        await allocationCreateService.ExecuteReversalAsync(
            contractKey, returnPurchase, returnPurchase.NetWeight, userName);
    }

    /// <summary>
    /// Contrato que a 8 original consumiu — o da alocação dela; na ausência (ou em mais de um),
    /// o contrato da liberação de origem.
    /// </summary>
    private async Task<Guid> ResolveOriginContractKeyAsync(Plan plan)
    {
        var allocated = await db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == plan.Purchase!.Key)
            .Select(x => x.PurchaseContractKey)
            .Distinct()
            .ToListAsync();

        return allocated.Count == 1 ? allocated[0] : plan.Source.PurchaseContractKey;
    }

    // ───────────────────────────── Expedição nova ─────────────────────────────

    private async Task CreateAtDestinationAsync(Plan plan, string userName)
    {
        var original = plan.Sales;
        var target = plan.Target;
        var contract = target.PurchaseContract!;

        // d. Nova 7.
        var newSales = StorageTransactionCopyFactory.CreateFrom(original, userName);
        newSales.TransactionType = StorageTransactionType.SalesShipment;
        newSales.TransactionStatus = StorageTransactionsStatus.Pending;
        StampLoadDate(newSales, plan);
        SetWeights(newSales, plan.NewQuantity);
        ApplyDestination(newSales, plan);
        newSales.StorageAddressCode = plan.TargetLot?.Code;
        ClearDocumentLinks(newSales);

        await storageCreateService.ExecuteAsync(
            newSales, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
        await storageConfirmedService.ExecuteAsync(newSales, userName, CommitMode.Deferred, true);
        KeepNames(newSales, contract.CardName, target.DeliveryLocationName);

        // Assume a vaga da original na carga e no faturamento: status, nota e o consumo da
        // liberação de VENDA. A quantidade faturada passa a ser a da Expedição nova.
        newSales.TransactionStatus = original.TransactionStatus;
        newSales.SalesInvoiceKey = original.SalesInvoiceKey;
        newSales.SalesShipmentReleaseKey = original.SalesShipmentReleaseKey;
        newSales.IsInvoiced = original.IsInvoiced;
        newSales.InvoicedAt = original.InvoicedAt;
        newSales.InvoiceQty = original.SalesInvoiceKey != null || original.IsInvoiced
            ? plan.NewQuantity
            : original.InvoiceQty;
        newSales.ShipmentLoadKey = plan.Load.Key;
        plan.NewSales = newSales;

        // e. Nova 8 — só quando o destino tem perna de compra.
        StorageTransaction? newPurchase = null;
        if (!ReleaseOriginRules.ShipsWithoutPurchaseLeg(target.Origin))
            newPurchase = await CreatePurchaseLegAsync(plan, userName);

        // f. Vínculo da Expedição nova.
        await db.Context.ShippingTransactions.AddAsync(new ShippingTransaction
        {
            PurchaseStorageTransaction = newPurchase,
            SalesStorageTransaction = newSales,
            CreatedBy = userName,
            UpdatedBy = userName,
        });
    }

    /// <summary>
    /// Perna de compra da Expedição nova: mesmo caminho da Expedição de Grãos — criar Pending,
    /// confirmar (descontos pela tabela e inspeções da 8 original, quando houver) e alocar.
    /// </summary>
    private async Task<StorageTransaction> CreatePurchaseLegAsync(Plan plan, string userName)
    {
        var newSales = plan.NewSales!;
        var contract = plan.Target.PurchaseContract!;

        var purchase = StorageTransactionCopyFactory.CreateFrom(newSales, userName);

        if (plan.Purchase != null)
        {
            purchase.QualityInspections.Clear();
            foreach (var inspection in plan.Purchase.QualityInspections)
                purchase.QualityInspections.Add(
                    StorageTransactionQualityInspectionCopyFactory.CreateFrom(inspection, purchase));
            purchase.ProcessingCostCode = plan.Purchase.ProcessingCostCode;
        }

        purchase.TransactionType = StorageTransactionType.Purchase;
        purchase.TransactionStatus = StorageTransactionsStatus.Pending;
        StampLoadDate(purchase, plan);
        SetWeights(purchase, plan.NewQuantity);
        ApplyDestination(purchase, plan);
        // Só a perna de SAÍDA carrega lote (ver ShippingTransactionsCreateService).
        purchase.StorageAddressCode = null;
        ClearDocumentLinks(purchase);
        ApplyProducerInvoice(purchase, plan);

        await storageCreateService.ExecuteAsync(
            purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
        await storageConfirmedService.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);
        KeepNames(purchase, contract.CardName, plan.Target.DeliveryLocationName);
        // As tarifas de serviço já estão na 8 original: precificar de novo dobraria a soma
        // dos relatórios de serviço.
        ZeroServicePrices(purchase);

        plan.NewPurchase = purchase;

        // Alocação soma pelo BANCO: tudo que está pendente precisa estar gravado antes.
        await db.SaveChangesAsync();

        if (purchase.NetWeight > contract.AvaiableVolume)
            throw new ApplicationException(
                $"O contrato {contract.Code} não tem saldo para {purchase.NetWeight:N3} " +
                $"(disponível {contract.AvaiableVolume:N3}).");

        await allocationCreateService.ExecuteAsync(contract.Key, purchase, purchase.NetWeight, userName);

        return purchase;
    }

    // ───────────────────────────── Documento e narrativa ─────────────────────────────

    private void RegisterChange(Plan plan, Guid operationGroupKey, string reason, string userName)
    {
        var change = new ShippingReleaseChange
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = plan.Load.Key,
            OperationGroupKey = operationGroupKey,
            OriginalSalesStorageTransactionKey = plan.Sales.Key,
            OriginalPurchaseStorageTransactionKey = plan.Purchase?.Key,
            ReturnSalesStorageTransactionKey = plan.ReturnSales!.Key,
            ReturnPurchaseStorageTransactionKey = plan.ReturnPurchase?.Key,
            NewSalesStorageTransactionKey = plan.NewSales!.Key,
            NewPurchaseStorageTransactionKey = plan.NewPurchase?.Key,
            SourceShipmentReleaseKey = plan.Source.Key,
            TargetShipmentReleaseKey = plan.Target.Key,
            OriginalQuantity = plan.Sales.GrossWeight,
            NewQuantity = plan.NewQuantity,
            Reason = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName,
        };

        db.Context.ShippingReleaseChanges.Add(change);

        plan.Sales.ReplacedByShippingReleaseChangeKey = change.Key;
        if (plan.Purchase != null)
        {
            plan.Purchase.ReplacedByShippingReleaseChangeKey = change.Key;
            Touch(plan.Purchase, userName);
        }

        foreach (var generated in new[] { plan.ReturnSales, plan.ReturnPurchase, plan.NewSales, plan.NewPurchase })
        {
            if (generated != null)
                generated.ShippingReleaseChangeKey = change.Key;
        }
    }

    private void RegisterMovement(Plan plan, string reason, string userName)
    {
        var from = plan.Source.PurchaseContract!;
        var to = plan.Target.PurchaseContract!;
        var newSales = plan.NewSales!;

        movementLog.Register(
            plan.Load.Key,
            ShipmentLoadMovementType.ReleaseChanged,
            plan.NewQuantity - plan.Sales.GrossWeight,
            plan.Load.AvailableQuantity,
            $"Romaneio {plan.Sales.Code} substituído por {newSales.Code}: " +
            $"contrato {from.Code} ({from.CardName}), armazém {plan.Sales.WarehouseCode} → " +
            $"contrato {to.Code} ({to.CardName}), armazém {plan.Target.DeliveryLocationCode}; " +
            $"estorno {plan.ReturnSales!.Code}.",
            userName,
            movementContext: new ShipmentLoadMovementContext(
                CardCode: newSales.CardCode,
                CardName: newSales.CardName,
                WarehouseCode: newSales.WarehouseCode,
                WarehouseName: newSales.WarehouseName,
                Reason: reason,
                StorageTransactionKey: newSales.Key));
    }

    // ───────────────────────────── Auxiliares ─────────────────────────────

    /// <summary>Todos os lançamentos da troca têm a data da carga e a hora da Expedição original.</summary>
    private static void StampLoadDate(StorageTransaction st, Plan plan)
    {
        st.TransactionDate = plan.Load.LoadDate.Date;
        st.TransactionTime = plan.Sales.TransactionTime;
    }

    private static void SetWeights(StorageTransaction st, decimal quantity)
    {
        st.GrossWeight = quantity;
        st.NetWeight = quantity;
        st.DryingDiscount = 0m;
        st.CleaningDiscount = 0m;
        st.OthersDicount = 0m;
    }

    private static void ApplyDestination(StorageTransaction st, Plan plan)
    {
        var contract = plan.Target.PurchaseContract!;
        st.WarehouseCode = plan.Target.DeliveryLocationCode;
        st.WarehouseName = plan.Target.DeliveryLocationName;
        st.CardCode = contract.CardCode;
        st.CardName = contract.CardName;
        st.ShipmentReleaseKey = plan.Target.Key;
    }

    private static void ZeroServicePrices(StorageTransaction st)
    {
        st.DryingServicePrice = 0m;
        st.CleaningServicePrice = 0m;
        st.ReceiptServicePrice = 0m;
        st.ShipmentPrice = 0m;
    }

    /// <summary>
    /// NF do produtor na perna 8 nova. A cópia vem da 7 nova, que carrega dados da NOSSA nota ou
    /// a chave da NF do produtor original — nada disso vale para outro fornecedor. Só herda da 8
    /// original quando o fornecedor do destino é o mesmo.
    /// </summary>
    private static void ApplyProducerInvoice(StorageTransaction purchase, Plan plan)
    {
        var original = plan.Purchase;
        var sameProducer = original != null &&
                           string.Equals(original.CardCode, purchase.CardCode, StringComparison.OrdinalIgnoreCase);

        purchase.InvoiceNumber = sameProducer ? original!.InvoiceNumber : null;
        purchase.InvoiceSerie = sameProducer ? original!.InvoiceSerie : null;
        purchase.InvoiceQty = sameProducer ? original!.InvoiceQty : 0m;
        purchase.ChaveNFe = sameProducer ? original!.ChaveNFe : null;
    }

    private static void ClearDocumentLinks(StorageTransaction st)
    {
        st.ShipmentLoadKey = null;
        st.SalesInvoiceKey = null;
        st.RefusedFromShipmentLoadKey = null;
        st.GeneratedByReturnInvoiceKey = null;
        st.ReturnInvoiceKey = null;
        st.ReplacedByShippingReleaseChangeKey = null;
        st.ShippingReleaseChangeKey = null;
    }

    /// <summary>
    /// O create resolve nome de parceiro/armazém pelos cadastros e grava null quando o código não
    /// é encontrado (ex.: SAP fora); nesse caso fica o nome que já conhecemos.
    /// </summary>
    private static void KeepNames(StorageTransaction st, string? cardName, string? warehouseName)
    {
        st.CardName ??= cardName;
        st.WarehouseName ??= warehouseName;
    }

    private static void Touch(StorageTransaction st, string userName)
    {
        st.UpdatedAt = DateTime.Now;
        st.UpdatedBy = userName;
    }
}
