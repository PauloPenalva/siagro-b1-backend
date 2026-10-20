using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// Na Zeus, <c>NfeDistDFeInteresse</c> trata qualquer <c>nSU</c> diferente de "0" como consulta de UM NSU (consNSU)
/// e descarta o distNSU. Passar string vazia fazia a produção perguntar sempre pelo NSU 0: nenhum documento, nunca.
/// </summary>
public class NfeDistributionRequestTests
{
    [Fact]
    public void Nsu_query_sends_the_cursor_and_no_single_nsu()
    {
        var (ultNsu, nsu, key) = ZeusNfeSefazClient.DistributionByNsuArguments(50);

        Assert.Equal("000000000000050", ultNsu);
        Assert.Equal("0", nsu);
        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void Key_query_sends_no_single_nsu()
    {
        var (_, nsu, key) = ZeusNfeSefazClient.DistributionByKeyArguments("35261012345678000199550010000000011000000010");

        Assert.Equal("0", nsu);
        Assert.Equal("35261012345678000199550010000000011000000010", key);
    }
}
