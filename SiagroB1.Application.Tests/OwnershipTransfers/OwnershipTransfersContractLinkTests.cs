using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.OwnershipTransfers;

/// <summary>
/// O lado COMERCIAL da transferência: confirmar com contrato emite uma liberação de
/// embarque já liberada e COM saldo, sem tocar no eixo de alocação do contrato —
/// quem aloca é o Purchase(8) que a Expedição de Grãos cria depois.
/// </summary>
public class OwnershipTransfersContractLinkTests
{
    private readonly OwnershipTransfersTestContext _ctx = new();

    private async Task<(OwnershipTransfer Transfer, PurchaseContract Contract)> SeedAsync(
        decimal quantity = 1000m,
        decimal totalVolume = 10_000m,
        decimal allocatedVolume = 0m,
        ContractStatus contractStatus = ContractStatus.Approved,
        string contractItem = "SOJA",
        string contractUom = "KG",
        StorageOwnershipType originType = StorageOwnershipType.ThirdParty,
        StorageOwnershipType destinationType = StorageOwnershipType.OwnedInOurCustody,
        bool withContract = true)
    {
        var origin = OwnershipTransfersTestContext.Lot("LOTE-ORIG", "P0001", originType);
        var destination = OwnershipTransfersTestContext.Lot("LOTE-DEST", "E0001", destinationType);
        var contract = OwnershipTransfersTestContext.Contract(
            totalVolume, allocatedVolume, contractStatus, contractItem, contractUom);

        var transfer = OwnershipTransfersTestContext.Transfer(origin, destination, quantity);
        if (withContract)
            transfer.PurchaseContractKey = contract.Key;

        _ctx.Db.Context.StorageAddresses.AddRange(origin, destination);
        _ctx.Db.Context.PurchaseContracts.Add(contract);
        _ctx.Db.Context.OwnershipTransfers.Add(transfer);
        await _ctx.Db.Context.SaveChangesAsync();

        return (transfer, contract);
    }

    private Task<ShipmentRelease?> ReleaseOfAsync(Guid transferKey) =>
        _ctx.Db.Context.ShipmentReleases
            .AsNoTracking().FirstOrDefaultAsync(x => x.OwnershipTransferKey == transferKey);

    [Fact]
    public async Task Confirm_WithoutContract_CreatesNoRelease()
    {
        var (transfer, _) = await SeedAsync(withContract: false);

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        Assert.Empty(await _ctx.Db.Context.ShipmentReleases.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Confirm_WithContract_EmitsAnActivedReleaseWithBalance()
    {
        var (transfer, contract) = await SeedAsync(quantity: 1200m);

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var release = await ReleaseOfAsync(transfer.Key);
        Assert.NotNull(release);

        Assert.Equal(contract.Key, release.PurchaseContractKey);
        Assert.Equal(ReleaseStatus.Actived, release.Status);
        Assert.Equal(1200m, release.ReleasedQuantity);
        // Com saldo: a mercadoria ainda precisa ser embarcada para faturamento.
        Assert.Equal(decimal.Zero, release.ShippedQuantity);
        Assert.Equal(1200m, release.AvailableQuantity);
        Assert.Equal("tester", release.ApprovedBy);
    }

    [Fact]
    public async Task Confirm_StampsTheOriginAndTheOwnLotOnTheRelease()
    {
        var (transfer, _) = await SeedAsync();

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var release = await ReleaseOfAsync(transfer.Key);
        Assert.NotNull(release);

        Assert.Equal(ReleaseOrigin.OwnershipTransfer, release.Origin);
        Assert.Equal(transfer.Key, release.OwnershipTransferKey);
        // Sem o lote, a Expedição de Grãos não drenaria o estoque próprio.
        Assert.Equal("LOTE-DEST", release.StorageAddressCode);
        Assert.Equal("01", release.DeliveryLocationCode);
    }

    /// <summary>
    /// A regra central: a mercadoria FOI entregue, então o saldo físico do contrato
    /// cai no confirm — e a liberação continua com saldo a carregar, porque o grão
    /// ainda precisa ser embarcado para faturamento.
    /// </summary>
    [Fact]
    public async Task Confirm_DebitsThePhysicalBalanceAndKeepsTheReleaseLoadable()
    {
        var (transfer, contract) = await SeedAsync(quantity: 1000m, totalVolume: 10_000m);

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var reloaded = await _ctx.Db.Context.PurchaseContracts
            .AsNoTracking().Include(x => x.ShipmentReleases)
            .SingleAsync(x => x.Key == contract.Key);

        // Saldo físico debitado.
        Assert.Equal(1_000m, reloaded.AllocatedVolume);
        Assert.Equal(9_000m, reloaded.AvaiableVolume);
        Assert.Equal(9_000m, reloaded.TotalAvailableToRelease);

        // E a liberação segue inteira a carregar.
        var release = await ReleaseOfAsync(transfer.Key);
        Assert.NotNull(release);
        Assert.Equal(decimal.Zero, release.ShippedQuantity);
        Assert.Equal(1_000m, release.AvailableQuantity);
    }

    /// <summary>
    /// O romaneio de compra NÃO aponta para a liberação — é isso que mantém o
    /// ShippedQuantity zerado e a liberação com saldo a carregar.
    /// </summary>
    [Fact]
    public async Task Confirm_CreatesAPurchaseNotLinkedToTheRelease()
    {
        var (transfer, contract) = await SeedAsync(quantity: 1000m);

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var purchase = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.OwnershipTransferKey == transfer.Key &&
                              x.TransactionType == StorageTransactionType.Purchase);

        Assert.Null(purchase.ShipmentReleaseKey);
        Assert.Equal(1_000m, purchase.NetWeight);
        // Fornecedor do contrato, não o dono do lote: é dele que se está comprando.
        Assert.Equal(contract.CardCode, purchase.CardCode);

        var allocation = await _ctx.Db.Context.PurchaseContractsAllocations
            .AsNoTracking().SingleAsync();
        Assert.Equal(purchase.Key, allocation.StorageTransactionKey);
        Assert.Equal(1_000m, allocation.Volume);
    }

    [Fact]
    public async Task Confirm_RejectsWhenDestinationLotIsNotOwnStock()
    {
        var (transfer, _) = await SeedAsync(destinationType: StorageOwnershipType.ThirdParty);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_CONTRACT_DESTINATION_NOT_OWN", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsWhenOriginLotIsAlreadyOwnStock()
    {
        var (transfer, _) = await SeedAsync(originType: StorageOwnershipType.OwnedInOurCustody);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_CONTRACT_ORIGIN_IS_OWN", ex.Message);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.InApproval)]
    [InlineData(ContractStatus.Finished)]
    public async Task Confirm_RejectsAContractThatIsNotApproved(ContractStatus status)
    {
        var (transfer, _) = await SeedAsync(contractStatus: status);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_CONTRACT_NOT_APPROVED", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsAContractOfADifferentItem()
    {
        var (transfer, _) = await SeedAsync(contractItem: "MILHO");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_CONTRACT_ITEM_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsAContractOfADifferentUom()
    {
        var (transfer, _) = await SeedAsync(contractUom: "TON");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_CONTRACT_UOM_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectsQuantityAboveTheReleaseBalance()
    {
        var (transfer, _) = await SeedAsync(quantity: 1500m, totalVolume: 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_CONTRACT_RELEASE_BALANCE", ex.Message);
    }

    /// <summary>
    /// Saldo de liberação sobrando mas alocação esgotada: a liberação até caberia, mas
    /// o Purchase(8) da Expedição não conseguiria alocar depois.
    /// </summary>
    [Fact]
    public async Task Confirm_RejectsWhenTheAllocationAxisHasNoRoom()
    {
        var (transfer, _) = await SeedAsync(
            quantity: 1000m, totalVolume: 10_000m, allocatedVolume: 9_500m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));
        Assert.Equal("OWNERSHIP_TRANSFER_CONTRACT_ALLOCATION_BALANCE", ex.Message);
    }

    [Fact]
    public async Task Confirm_RejectionLeavesNoReleaseAndNoMovement()
    {
        var (transfer, _) = await SeedAsync(destinationType: StorageOwnershipType.ThirdParty);

        await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Confirm().ExecuteAsync(transfer.Key, "tester"));

        Assert.Empty(await _ctx.Db.Context.ShipmentReleases.AsNoTracking().ToListAsync());
        Assert.Empty(await _ctx.Db.Context.StorageTransactions.AsNoTracking().ToListAsync());

        var reloaded = await _ctx.Db.Context.OwnershipTransfers
            .AsNoTracking().SingleAsync(x => x.Key == transfer.Key);
        Assert.Equal(OwnershipTransferStatus.Open, reloaded.TransferStatus);
    }

    /// <summary>
    /// Com contrato são TRÊS romaneios: o par de custódia (lote origem/destino) mais
    /// a perna comercial que baixa o contrato.
    /// </summary>
    [Fact]
    public async Task Confirm_WritesCustodyPairPlusCommercialLeg()
    {
        var (transfer, _) = await SeedAsync(quantity: 800m);

        await _ctx.Confirm().ExecuteAsync(transfer.Key, "tester");

        var transactions = await _ctx.Db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.OwnershipTransferKey == transfer.Key).ToListAsync();

        Assert.Equal(3, transactions.Count);

        var shipment = Assert.Single(transactions, x => x.TransactionType == StorageTransactionType.Shipment);
        Assert.Equal("LOTE-ORIG", shipment.StorageAddressCode);

        var receipt = Assert.Single(transactions, x => x.TransactionType == StorageTransactionType.Receipt);
        Assert.Equal("LOTE-DEST", receipt.StorageAddressCode);

        // A perna comercial não carrega lote: o lote já foi movimentado pelo par acima.
        var purchase = Assert.Single(transactions, x => x.TransactionType == StorageTransactionType.Purchase);
        Assert.Null(purchase.StorageAddressCode);
    }
}
