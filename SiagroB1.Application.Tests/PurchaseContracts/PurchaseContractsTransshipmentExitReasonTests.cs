using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// GAC-1233: a linha informativa da saída de transbordo tem quantidade zero na aba "Entregas" (não baixa
/// o contrato); o peso do romaneio aparece no Motivo, junto do romaneio que gerou a liberação.
/// </summary>
public class PurchaseContractsTransshipmentExitReasonTests
{
    [Theory]
    [InlineData(7, "00017647", "440", "KG", "Saída do transbordo 00017647 — 440 KG")]
    [InlineData(7, "00017647", "46820.5", "KG", "Saída do transbordo 00017647 — 46.820,5 KG")]
    [InlineData(12, "00017647", "440", "KG", "Devolução ao transbordo 00017647 — 440 KG")]
    [InlineData(13, "00017647", "440", "KG", "Perda no transbordo 00017647 — 440 KG")]
    [InlineData(7, null, "440", null, "Saída do transbordo — 440")]
    [InlineData(7, "  ", "440", "KG", "Saída do transbordo — 440 KG")]
    public void Reason_names_the_generator_and_carries_the_weight(
        int transactionType, string? generatorCode, string netWeight, string? unitOfMeasureCode, string expected)
    {
        var reason = PurchaseContractsGetAllocationsByContractService.TransshipmentExitReason(
            transactionType, generatorCode,
            decimal.Parse(netWeight, System.Globalization.CultureInfo.InvariantCulture), unitOfMeasureCode);

        Assert.Equal(expected, reason);
    }
}
