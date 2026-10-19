using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Royalties;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.PurchaseContracts;

public sealed record PurchaseTransactionChangeResult(string ReturnCode, string NewCode);

/// <summary>
/// Troca de fornecedor/armazém de um romaneio de compra (tipo 8) na alocação: o romaneio foi
/// lançado com fornecedor e/ou armazém errados e o usuário escolhe um contrato de outro
/// fornecedor ou com outro Local de Entrega.
/// </summary>
/// <remarks>
/// <para>
/// <b>A troca é explícita, por romaneios de compensação</b> (mesmo desenho da troca de liberação,
/// GAC-1177), todos com a data e hora do romaneio original e amarrados por um
/// <see cref="PurchaseTransactionChange"/>. Seja <c>S</c> o saldo não alocado do original:
/// </para>
/// <list type="bullet">
/// <item><b>Original</b>: intacto; as alocações dele ficam e o saldo alocável cai em <c>S</c>
/// (<see cref="StorageTransaction.RecalculateAvailableVolume"/>).</item>
/// <item><b>Estorno na origem</b>: 9 do fornecedor/armazém do original, com <c>Net = S</c> e bruto e
/// descontos proporcionais; sem alocação e nunca alocável.</item>
/// <item><b>Romaneio novo</b>: 8 confirmado no fornecedor do contrato e no Local de Entrega dele,
/// com os pesos do estorno, alocado no contrato com o volume informado; o que sobrar fica pendente
/// de alocação nele.</item>
/// </list>
/// <para>
/// <b>Ciclo de FKs:</b> o documento da troca aponta os romaneios gerados e eles apontam de volta o
/// documento. Os romaneios são gravados primeiro sem a chave, e o documento entra num segundo
/// <c>SaveChanges</c>, junto com os carimbos — como em <see cref="ShippingTransactionsChangeReleaseService"/>.
/// </para>
/// </remarks>
public class PurchaseTransactionsChangeSupplierService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageCreateService,
    StorageTransactionsConfirmedService storageConfirmedService,
    PurchaseContractsAllocationCreateService allocationCreateService,
    ShippingReleaseChangeReconciliationGuard reconciliationGuard)
{
    private const string NotChangeableMessage =
        "Só romaneio de compra confirmado com saldo a alocar pode ter o fornecedor/armazém trocado.";

    public async Task<PurchaseTransactionChangeResult> ExecuteAsync(
        Guid storageTransactionKey,
        Guid purchaseContractKey,
        decimal volume,
        string? reason,
        string userName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo da troca de fornecedor/armazém.");

        reason = reason.Trim();

        // ── Validação completa antes de qualquer escrita ──
        var original = await db.Context.StorageTransactions
                           .Include(x => x.QualityInspections)
                           .ThenInclude(q => q.QualityAttrib)
                           .FirstOrDefaultAsync(x => x.Key == storageTransactionKey)
                       ?? throw new NotFoundException("Romaneio não encontrado.");

        var contract = await db.Context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == purchaseContractKey)
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        if (original.TransactionType != StorageTransactionType.Purchase ||
            original.TransactionStatus != StorageTransactionsStatus.Confirmed)
            throw new ApplicationException(NotChangeableMessage);

        // Expedição de Grãos e Entrada em armazenagem própria têm o próprio estorno/troca
        // (Trocar liberação da carga, estorno da entrada).
        if (await db.Context.ShippingTransactions.AnyAsync(x => x.PurchaseStorageTransactionKey == original.Key) ||
            await db.Context.StorageEntryTransactions.AnyAsync(x => x.PurchaseStorageTransactionKey == original.Key))
            throw new ApplicationException(
                $"O romaneio {original.Code} faz parte de outro fluxo (expedição ou entrada em armazenagem) " +
                "e não pode ter o fornecedor/armazém trocado por aqui.");

        // Só romaneio lançado na tela de romaneios (o romaneio novo de uma troca herda essa origem e pode
        // ser trocado de novo). Os gerados por outro fluxo — transferência de titularidade, carga,
        // conferência de saldo, descarga… — têm o próprio estorno.
        if (original.TransactionOrigin != TransactionCode.StorageTransaction)
            throw new ApplicationException(
                $"O romaneio {original.Code} foi gerado por outro fluxo e não pode ter o fornecedor/armazém " +
                "trocado por aqui.");

        // Saldo pelo BANCO, pela mesma regra de RecalculateAvailableVolume (não pela coluna gravada).
        var allocated = await db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == original.Key)
            .SumAsync(x => decimal.Abs(x.Volume));
        var alreadyChanged = await PurchaseTransactionChangeQuery.ChangedQuantityAsync(db.Context, original.Key);
        var quantity = original.NetWeight - allocated - alreadyChanged;

        if (quantity <= decimal.Zero)
            throw new ApplicationException(NotChangeableMessage);

        if (!string.Equals(contract.ItemCode, original.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException("O contrato é de outro produto.");

        if (string.Equals(contract.CardCode, original.CardCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(contract.DeliveryLocationCode, original.WarehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException("Fornecedor e armazém são os mesmos do contrato: use a alocação normal.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException("Contrato encerrado ou não aprovado: não é possível alocar.");

        if (volume <= decimal.Zero)
            throw new ApplicationException("Volume de alocação inválido.");

        if (volume > quantity)
            throw new ApplicationException("O volume informado é maior que o saldo disponível do romaneio.");

        if (volume > contract.AvaiableVolume)
            throw new ApplicationException(
                "O volume informado é maior que o saldo disponível do contrato de compra.");

        // Troca total = o original inteiro muda de dono (sem alocação nem troca anterior): só aí a
        // NF do produtor e os vínculos da conciliação vão junto. Parcial com nota não tem como
        // dividir a nota — o usuário desaloca e faz a troca total (decisão 6 do usuário).
        var isFullChange = quantity == original.NetWeight;
        if (!isFullChange &&
            (HasProducerInvoice(original) ||
             await db.Context.PurchaseFiscalReconciliations.AnyAsync(x => x.StorageTransactionKey == original.Key)))
            throw new ApplicationException(
                "Romaneio com nota fiscal e parte alocada: remova as alocações antes de trocar o fornecedor/armazém.");

        // O original sai de cena na parte trocada: com baixa ITS ativa os royalties ficariam
        // apontando para um romaneio estornado; reconciliado em remessa, o retorno apontaria o nada.
        await RoyaltyRules.EnsureNoActiveWriteOffAsync(db.Context, original.Key);
        await StorageDepositRules.EnsureTransactionsNotReconciledAsync(db.Context, [original.Key]);

        // Estorno e romaneio novo são retroativos (data do romaneio): conferência aprovada na
        // origem ou no destino a partir dessa data já fechou a Perda/Sobra sobre o saldo antigo.
        await reconciliationGuard.EnsureNoApprovedReconciliationAsync(
            original.ItemCode,
            [original.WarehouseCode, contract.DeliveryLocationCode],
            original.TransactionDate ?? DateTime.Today,
            dateLabel: "data do romaneio",
            action: "trocar o fornecedor/armazém");

        try
        {
            await db.BeginTransactionAsync();

            var changeKey = Guid.NewGuid();
            var returnPurchase = await CreateReturnAsync(original, quantity, userName);
            var newPurchase = await CreateNewAsync(original, returnPurchase, contract, isFullChange, changeKey, userName);

            // Alocação soma pelo BANCO: tudo que está pendente precisa estar gravado antes.
            await db.SaveChangesAsync();

            await allocationCreateService.ExecuteAsync(contract.Key, newPurchase, volume, userName);

            // Documento da troca + carimbos (segundo SaveChanges: ver <remarks>).
            db.Context.PurchaseTransactionChanges.Add(new PurchaseTransactionChange
            {
                Key = changeKey,
                OriginalStorageTransactionKey = original.Key,
                ReturnStorageTransactionKey = returnPurchase.Key,
                NewStorageTransactionKey = newPurchase.Key,
                PurchaseContractKey = contract.Key,
                OriginalCardCode = original.CardCode,
                OriginalWarehouseCode = original.WarehouseCode,
                NewCardCode = newPurchase.CardCode,
                NewWarehouseCode = newPurchase.WarehouseCode,
                Quantity = quantity,
                AllocatedVolume = volume,
                Reason = reason,
                CreatedAt = DateTime.Now,
                CreatedBy = userName,
                UpdatedAt = DateTime.Now,
                UpdatedBy = userName,
            });
            returnPurchase.PurchaseTransactionChangeKey = changeKey;
            newPurchase.PurchaseTransactionChangeKey = changeKey;

            if (isFullChange)
                await MoveInvoiceAsync(original, newPurchase);

            await db.SaveChangesAsync();

            // Saldo do original já com a troca gravada (fonte única: RecalculateAvailableVolume).
            original.RecalculateAvailableVolume(
                allocated, await PurchaseTransactionChangeQuery.ChangedQuantityAsync(db.Context, original.Key));
            Touch(original, userName);

            await db.SaveChangesAsync();
            await db.CommitAsync();

            return new PurchaseTransactionChangeResult(returnPurchase.Code!, newPurchase.Code!);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    // ───────────────────────────── Estorno na origem ─────────────────────────────

    private async Task<StorageTransaction> CreateReturnAsync(StorageTransaction original, decimal quantity, string userName)
    {
        var returnPurchase = StorageTransactionCopyFactory.CreateFrom(original, userName);
        returnPurchase.TransactionType = StorageTransactionType.PurchaseReturn;
        returnPurchase.TransactionStatus = StorageTransactionsStatus.Pending;
        StampOriginalDate(returnPurchase, original);
        returnPurchase.StorageAddressCode = null;
        returnPurchase.ShipmentReleaseKey = null;
        ClearDocumentLinks(returnPurchase);
        // A nota do produtor é do original (ou vai para o romaneio novo): o estorno não a carrega.
        ClearProducerInvoice(returnPurchase);

        await storageCreateService.ExecuteAsync(
            returnPurchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
        // Tipo 9 cai no ramo de compra do confirm (CalculateReceipt pela tabela/inspeções).
        await storageConfirmedService.ExecuteAsync(returnPurchase, userName, CommitMode.Deferred);
        KeepNames(returnPurchase, original.CardName, original.WarehouseName);

        // O estorno ESPELHA a parte trocada do original: a tabela de custo pode ter mudado desde
        // a confirmação dele, e qualquer diferença deixaria o armazém de origem com resíduo.
        // Serviço não se estorna por aqui — as tarifas ficam zeradas.
        ApplyProportionalWeights(returnPurchase, original, quantity);
        returnPurchase.AvaiableVolumeToAllocate = decimal.Zero;
        ZeroServicePrices(returnPurchase);

        return returnPurchase;
    }

    // ───────────────────────────── Romaneio novo ─────────────────────────────

    private async Task<StorageTransaction> CreateNewAsync(
        StorageTransaction original,
        StorageTransaction returnPurchase,
        PurchaseContract contract,
        bool isFullChange,
        Guid changeKey,
        string userName)
    {
        // A cópia leva inspeções, tabela de custo, royalty e dados de pesagem (veículo, motorista, ticket).
        var newPurchase = StorageTransactionCopyFactory.CreateFrom(original, userName);
        newPurchase.TransactionType = StorageTransactionType.Purchase;
        newPurchase.TransactionStatus = StorageTransactionsStatus.Pending;
        StampOriginalDate(newPurchase, original);
        newPurchase.CardCode = contract.CardCode;
        newPurchase.CardName = contract.CardName;
        newPurchase.WarehouseCode = contract.DeliveryLocationCode;
        newPurchase.WarehouseName = contract.DeliveryLocationName;
        newPurchase.StorageAddressCode = null;
        newPurchase.ShipmentReleaseKey = null;
        ClearDocumentLinks(newPurchase);
        // A NF do produtor passa para o romaneio novo só na troca total (decisões 3 e 6).
        if (!isFullChange)
            ClearProducerInvoice(newPurchase);

        await storageCreateService.ExecuteAsync(
            newPurchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
        // A trava de royalty isenta o romaneio gerado por troca cujo original não tem situação
        // (lançado antes do controle). A chave vale só durante a confirmação e sai antes de
        // qualquer SaveChanges, para não referenciar uma troca ainda não gravada.
        newPurchase.PurchaseTransactionChangeKey = changeKey;
        await storageConfirmedService.ExecuteAsync(newPurchase, userName, CommitMode.Deferred);
        newPurchase.PurchaseTransactionChangeKey = null;
        KeepNames(newPurchase, contract.CardName, contract.DeliveryLocationName);

        // Mesmos pesos do estorno: o que sai da origem é exatamente o que entra no destino.
        CopyWeights(newPurchase, returnPurchase);
        newPurchase.AvaiableVolumeToAllocate = newPurchase.NetWeight;
        // As tarifas de serviço já estão no original: precificar de novo dobraria a soma dos
        // relatórios de serviço.
        ZeroServicePrices(newPurchase);

        return newPurchase;
    }

    /// <summary>
    /// Troca total: a NF do produtor sai do original e fica no romaneio novo; os vínculos da
    /// Conciliação Fiscal de Compras do original são reapontados para o novo. Só a chave do
    /// romaneio muda — linha do Documento de Entrada, fornecedor do documento e financeiro ficam
    /// como estão (sem alocação, o original só pode estar em linhas sem contrato).
    /// </summary>
    private async Task MoveInvoiceAsync(StorageTransaction original, StorageTransaction newPurchase)
    {
        var links = await db.Context.PurchaseFiscalReconciliations
            .Where(x => x.StorageTransactionKey == original.Key)
            .ToListAsync();

        foreach (var link in links)
            link.StorageTransactionKey = newPurchase.Key;

        // Qualquer mudança nos vínculos limpa a confirmação manual da conciliação.
        if (links.Count > 0)
        {
            original.FiscalReconciledAt = null;
            original.FiscalReconciledBy = null;
        }

        ClearProducerInvoice(original);
    }

    // ───────────────────────────── Auxiliares ─────────────────────────────

    /// <summary>
    /// Pesos da parte trocada: com <c>quantity</c> igual ao líquido, espelho exato do original;
    /// senão, bruto e descontos × <c>r = quantity / NetWeight</c>, arredondados a 3 casas.
    /// </summary>
    /// <remarks>
    /// Decisão do construtor: o líquido fecha EXATAMENTE em <c>quantity</c> — é ele que anula o
    /// original no saldo do armazém e no saldo alocável. O resíduo do arredondamento (no máximo
    /// alguns gramas) é absorvido em <see cref="StorageTransaction.OthersDicount"/>, para manter
    /// <c>Net = Bruto − Secagem − Limpeza − Outros</c>.
    /// </remarks>
    private static void ApplyProportionalWeights(StorageTransaction target, StorageTransaction original, decimal quantity)
    {
        if (quantity == original.NetWeight)
        {
            CopyWeights(target, original);
            return;
        }

        var ratio = quantity / original.NetWeight;
        target.GrossWeight = Round3(original.GrossWeight * ratio);
        target.DryingDiscount = Round3(original.DryingDiscount * ratio);
        target.CleaningDiscount = Round3(original.CleaningDiscount * ratio);
        target.NetWeight = quantity;
        target.OthersDicount = target.GrossWeight - target.DryingDiscount - target.CleaningDiscount - quantity;
    }

    private static decimal Round3(decimal value) => decimal.Round(value, 3, MidpointRounding.AwayFromZero);

    private static void CopyWeights(StorageTransaction target, StorageTransaction source)
    {
        target.GrossWeight = source.GrossWeight;
        target.DryingDiscount = source.DryingDiscount;
        target.CleaningDiscount = source.CleaningDiscount;
        target.OthersDicount = source.OthersDicount;
        target.NetWeight = source.NetWeight;
    }

    private static bool HasProducerInvoice(StorageTransaction st) =>
        !string.IsNullOrWhiteSpace(st.InvoiceNumber) || !string.IsNullOrWhiteSpace(st.ChaveNFe);

    private static void ClearProducerInvoice(StorageTransaction st)
    {
        st.InvoiceNumber = null;
        st.InvoiceSerie = null;
        st.InvoiceQty = decimal.Zero;
        st.ChaveNFe = null;
    }

    /// <summary>Estorno e romaneio novo têm a data e a hora do romaneio original (decisão 2).</summary>
    private static void StampOriginalDate(StorageTransaction st, StorageTransaction original)
    {
        st.TransactionDate = original.TransactionDate;
        st.TransactionTime = original.TransactionTime;
    }

    private static void ZeroServicePrices(StorageTransaction st)
    {
        st.DryingServicePrice = decimal.Zero;
        st.CleaningServicePrice = decimal.Zero;
        st.ReceiptServicePrice = decimal.Zero;
        st.ShipmentPrice = decimal.Zero;
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
        st.PurchaseTransactionChangeKey = null;
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
