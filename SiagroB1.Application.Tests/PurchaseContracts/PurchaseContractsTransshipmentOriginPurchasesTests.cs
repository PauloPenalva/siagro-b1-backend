using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// GAC-1233: a compra original da saída de transbordo vai para a tela em dois textos paralelos (chaves e
/// códigos, separados por vírgula), na mesma ordem, para a coluna montar um link por romaneio.
/// </summary>
public class PurchaseContractsTransshipmentOriginPurchasesTests
{
    private static readonly Guid Exit = Guid.Parse("94098dc7-128d-4b8f-9403-8553cf32d956");
    private static readonly Guid PurchaseA = Guid.Parse("a3d10515-fbe7-46f6-929c-e4fce7092b97");
    private static readonly Guid PurchaseB = Guid.Parse("b3d10515-fbe7-46f6-929c-e4fce7092b97");

    [Fact]
    public void Single_purchase_becomes_one_key_and_one_code()
    {
        var map = PurchaseContractsGetAllocationsByContractService.GroupOriginPurchases(
            [(Exit, PurchaseA, "00017645")]);

        Assert.Equal(PurchaseA.ToString(), map[Exit].Keys);
        Assert.Equal("00017645", map[Exit].Codes);
    }

    [Fact]
    public void Several_purchases_are_ordered_by_code_and_deduplicated()
    {
        var map = PurchaseContractsGetAllocationsByContractService.GroupOriginPurchases(
        [
            (Exit, PurchaseB, "00017700"),
            (Exit, PurchaseA, "00017645"),
            (Exit, PurchaseA, "00017645"),
        ]);

        Assert.Equal($"{PurchaseA},{PurchaseB}", map[Exit].Keys);
        Assert.Equal("00017645,00017700", map[Exit].Codes);
    }

    [Fact]
    public void Exit_without_origin_purchase_is_absent()
    {
        var map = PurchaseContractsGetAllocationsByContractService.GroupOriginPurchases([]);

        Assert.False(map.ContainsKey(Exit));
    }
}
