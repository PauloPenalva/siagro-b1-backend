using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

/// <summary>Quanto de um romaneio de origem voltou ao armazém.</summary>
public sealed record ReturnedShipmentShare(StorageTransaction Shipment, decimal Quantity);

/// <summary>
/// O que a devolução conseguiu (e não conseguiu) transformar em liberação.
/// </summary>
/// <param name="Releases">Uma por contrato de compra distinto, já pronta para ser gravada.</param>
/// <param name="UntraceableQuantity">Volume sem contrato rastreável — fica sem liberação.</param>
/// <param name="Note">Texto para o <c>Comments</c> do romaneio, quando houve volume órfão.</param>
public sealed record ReturnReleaseBuildResult(
    IReadOnlyList<ShipmentRelease> Releases,
    decimal UntraceableQuantity,
    string? Note);

/// <summary>
/// Emite as liberações de embarque de uma devolução ao armazém, para que a mercadoria que
/// voltou reapareça na Expedição de Grãos em vez de ficar presa como saldo físico.
/// </summary>
/// <remarks>
/// <b>Rastreia o contrato de compra a partir da origem</b>, em duas cadeias:
/// <list type="number">
/// <item><b>Curta</b> — o <see cref="StorageTransactionType.SalesShipment"/> devolvido carrega a
/// <c>ShipmentReleaseKey</c> (copiada da perna de compra por <c>StorageTransactionCopyFactory</c>),
/// e a liberação sabe o contrato.</item>
/// <item><b>Longa</b> — sem aquela chave, vai por <c>SHIPPING_TRANSACTIONS</c> (par
/// saída/compra) até a alocação do <see cref="StorageTransactionType.Purchase"/>. Cobre o
/// romaneio cuja liberação original foi apagada.</item>
/// </list>
/// <para>
/// <b>Sem contrato rastreável a devolução NÃO falha.</b> O crédito físico já aconteceu — o
/// caminhão descarregou. Recusar aqui transformaria dado legado em bloqueio operacional de um
/// fluxo que hoje funciona. O volume órfão fica sem liberação e o motivo vai para o
/// <c>Comments</c> do romaneio, para o operador criar a liberação à mão.
/// </para>
/// <para>
/// <b>Uma liberação por CONTRATO, não por devolução.</b> A entrada no armazém continua sendo uma
/// só (é um evento físico), mas os romaneios devolvidos podem vir de contratos diferentes e uma
/// liberação exige um contrato. Escolher um só faria o operador ver a carga inteira pendurada num
/// fornecedor de quem veio apenas parte do grão.
/// </para>
/// </remarks>
public class ShipmentReleasesFromReturnService(AppDbContext context)
{
    /// <summary>Folga de arredondamento, igual à usada no resto do módulo.</summary>
    private const decimal Tolerance = 0.001m;

    public async Task<ReturnReleaseBuildResult> BuildAsync(
        StorageTransaction entry,
        IReadOnlyList<ReturnedShipmentShare> shares,
        string warehouseCode,
        string? warehouseName,
        string userName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(shares);

        var byContract = new Dictionary<Guid, decimal>();
        var untraceable = decimal.Zero;
        var orphanCodes = new List<string>();

        foreach (var share in shares)
        {
            if (share.Quantity <= Tolerance)
                continue;

            var contract = await ResolveContractAsync(share.Shipment, entry);

            if (contract is null)
            {
                untraceable += share.Quantity;
                orphanCodes.Add(share.Shipment.Code ?? share.Shipment.Key.ToString());
                continue;
            }

            byContract[contract.Key] = byContract.GetValueOrDefault(contract.Key) + share.Quantity;
        }

        var releases = byContract
            .Select(pair => Create(
                entry, pair.Key, pair.Value, warehouseCode, warehouseName, shares, userName))
            .ToList();

        var note = untraceable > Tolerance
            ? $"Sem liberação de embarque para {untraceable:N3} " +
              $"(romaneio(s) {string.Join(", ", orphanCodes)} sem contrato de compra rastreável). " +
              "Crie a liberação manualmente pela tela de Liberações."
            : null;

        return new ReturnReleaseBuildResult(releases, untraceable, note);
    }

    /// <summary>
    /// Reparte <paramref name="totalQuantity"/> entre os romaneios em proporção ao peso, sem
    /// perder nem inventar gramas.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>O rateio inventa uma precisão que o dado não tem, e isso é deliberado.</b> Na recusa
    /// de carga a escolha do operador é por DOCUMENTO (<c>RefusalLine</c>), e a carga é um pool
    /// fungível: se ela leva 30 t do contrato P e 30 t do contrato Q e o cliente recusou 20 t,
    /// ninguém sabe de quais 20 t se trata. Metade para cada é a atribuição menos arbitrária
    /// disponível — não um cálculo exato.
    /// <para>
    /// O resto é distribuído por <b>maior fração</b>, e não jogado na primeira linha, para que a
    /// soma das liberações bata exatamente com o crédito do armazém. Divergir em gramas aqui
    /// vira um saldo que ninguém consegue explicar depois.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ReturnedShipmentShare> DistributeByWeight(
        IReadOnlyList<StorageTransaction> shipments, decimal totalQuantity)
    {
        ArgumentNullException.ThrowIfNull(shipments);

        if (shipments.Count == 0 || totalQuantity <= decimal.Zero)
            return [];

        if (shipments.Count == 1)
            return [new ReturnedShipmentShare(shipments[0], decimal.Round(totalQuantity, 3))];

        var totalWeight = shipments.Sum(x => x.GrossWeight);

        // Sem peso não há como ratear: divide igualmente, que é a única repartição neutra.
        if (totalWeight <= decimal.Zero)
        {
            var even = decimal.Round(totalQuantity / shipments.Count, 3, MidpointRounding.ToEven);
            var evenShares = shipments
                .Select(s => new ReturnedShipmentShare(s, even))
                .ToList();
            return ApplyRemainder(evenShares, totalQuantity);
        }

        var raw = shipments
            .Select(s => new
            {
                Shipment = s,
                Exact = totalQuantity * s.GrossWeight / totalWeight,
            })
            .ToList();

        var floored = raw
            .Select(x => new ReturnedShipmentShare(
                x.Shipment, decimal.Floor(x.Exact * 1000m) / 1000m))
            .ToList();

        // Maior fração primeiro: quem perdeu mais no truncamento recebe o milésimo de volta.
        var order = raw
            .Select((x, i) => new { Index = i, Fraction = x.Exact * 1000m - decimal.Floor(x.Exact * 1000m) })
            .OrderByDescending(x => x.Fraction)
            .Select(x => x.Index)
            .ToList();

        return ApplyRemainder(floored, totalQuantity, order);
    }

    #region Private Methods

    private static IReadOnlyList<ReturnedShipmentShare> ApplyRemainder(
        List<ReturnedShipmentShare> shares,
        decimal totalQuantity,
        IReadOnlyList<int>? order = null)
    {
        var missing = decimal.Round(totalQuantity - shares.Sum(x => x.Quantity), 3);
        var steps = (int)decimal.Round(missing * 1000m);
        var sequence = order ?? Enumerable.Range(0, shares.Count).ToList();

        for (var i = 0; steps > 0 && i < sequence.Count; i++, steps--)
        {
            var index = sequence[i];
            shares[index] = shares[index] with { Quantity = shares[index].Quantity + 0.001m };
        }

        // Sobra maior que uma volta na lista (só com muitos romaneios): joga na primeira.
        if (steps > 0)
            shares[0] = shares[0] with { Quantity = shares[0].Quantity + steps * 0.001m };

        return shares;
    }

    /// <summary>
    /// Contrato de compra que originou o grão devolvido, ou <c>null</c> quando não há como saber
    /// — ou quando o contrato encontrado não serve (ver os portões abaixo).
    /// </summary>
    private async Task<PurchaseContract?> ResolveContractAsync(
        StorageTransaction shipment, StorageTransaction entry)
    {
        var contractKey = await TraceContractKeyAsync(shipment);

        if (contractKey is null)
            return null;

        var contract = await context.PurchaseContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == contractKey.Value);

        if (contract is null)
            return null;

        // Portão do PRODUTO: ShipmentReleasesBalanceService agrupa a Expedição de Grãos por
        // PurchaseContract.ItemCode, não pelo item da liberação. Um contrato de produto
        // diferente faria a mercadoria devolvida aparecer na expedição do produto errado.
        if (!string.Equals(contract.ItemCode, entry.ItemCode, StringComparison.OrdinalIgnoreCase))
            return null;

        // Portão da FILIAL: a segunda etapa da Expedição agrupa por Branch.ShortName com INNER
        // JOIN. Sem filial em nenhum dos dois lados a liberação nasceria invisível — a mercadoria
        // ficaria presa de novo, agora de um jeito mais difícil de entender.
        if (string.IsNullOrWhiteSpace(contract.BranchCode) &&
            string.IsNullOrWhiteSpace(entry.BranchCode))
            return null;

        // Contrato Finished NÃO é motivo de recusa: a mercadoria precisa de porta de saída
        // independentemente do estado comercial do contrato que a originou.
        return contract;
    }

    private async Task<Guid?> TraceContractKeyAsync(StorageTransaction shipment)
    {
        // Cadeia curta: a perna de saída carrega a chave da liberação que a originou.
        if (shipment.ShipmentReleaseKey is { } releaseKey)
        {
            var contractKey = await context.ShipmentReleases
                .AsNoTracking()
                .Where(x => x.Key == releaseKey)
                .Select(x => (Guid?)x.PurchaseContractKey)
                .FirstOrDefaultAsync();

            if (contractKey is not null)
                return contractKey;
        }

        // Cadeia longa: par saída/compra → alocação do Purchase(8) → contrato.
        var purchaseKey = await context.ShippingTransactions
            .AsNoTracking()
            .Where(x => x.SalesStorageTransactionKey == shipment.Key)
            .Select(x => x.PurchaseStorageTransactionKey)
            .FirstOrDefaultAsync();

        if (purchaseKey is null)
            return null;

        return await context.PurchaseContractsAllocations
            .AsNoTracking()
            .Where(x => x.StorageTransactionKey == purchaseKey.Value)
            .Select(x => (Guid?)x.PurchaseContractKey)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Monta a liberação. Nasce <see cref="ReleaseStatus.Actived"/> COM saldo, como a de
    /// transferência de titularidade: o físico já está no armazém e só falta embarcá-lo.
    /// </summary>
    /// <remarks>
    /// Não passa por <c>ShipmentReleasesCreateService</c> nem por
    /// <c>ShipmentReleasesApprovationService</c>, de propósito: o primeiro força
    /// <c>Status = Pending</c> e chama <c>SaveChangesAsync</c> sozinho — o que quebraria a
    /// atomicidade da devolução —, e o segundo valida contra
    /// <c>TotalAvailableToReleaseWithoutProvisioning</c>, que é justamente a regra que não vale
    /// aqui (esta liberação não consome o contrato). Mesmo precedente de
    /// <c>OwnershipTransferShipmentReleaseFactory</c>.
    /// </remarks>
    private static ShipmentRelease Create(
        StorageTransaction entry,
        Guid contractKey,
        decimal quantity,
        string warehouseCode,
        string? warehouseName,
        IReadOnlyList<ReturnedShipmentShare> shares,
        string userName)
    {
        var codes = string.Join(", ", shares
            .Select(x => x.Shipment.Code)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct());

        var comments =
            $"Liberação gerada pela devolução ao armazém (romaneio {entry.Code}). " +
            (string.IsNullOrWhiteSpace(codes) ? string.Empty : $"Romaneio(s) de origem: {codes}. ") +
            "A mercadoria já está no armazém: o embarque desta liberação não debita o contrato.";

        return new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contractKey,
            BranchCode = entry.BranchCode,
            ReleaseDate = entry.TransactionDate ?? DateTime.Now.Date,

            ReleasedQuantity = quantity,
            ShippedQuantity = decimal.Zero,

            DeliveryLocationCode = warehouseCode,
            DeliveryLocationName = warehouseName,

            // Devolução é entrada em nível de ARMAZÉM: o romaneio tipo 12 nasce sem lote e o
            // saldo por endereço nem o credita. Sem lote a drenar, o embarque roda em nível de
            // armazém — ver ResolveReleaseLotAsync em ShippingTransactionsCreateService.
            StorageAddressCode = null,

            Status = ReleaseStatus.Actived,
            ApprovedAt = DateTime.Now,
            ApprovedBy = userName,

            Origin = ReleaseOrigin.SalesReturn,
            GeneratedByStorageTransactionKey = entry.Key,

            Comments = Truncate(comments, 500),

            CreatedAt = DateTime.Now,
            CreatedBy = userName,
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    #endregion
}
