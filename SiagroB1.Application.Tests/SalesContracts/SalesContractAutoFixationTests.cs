using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// A fixação automática do contrato de venda de preço fixo (FIX) é o espelho do preço já
/// acordado na negociação. Estes testes cobrem o mapeamento contrato → fixação, que é a parte
/// pura de <see cref="SalesContractsCreateService"/> — o restante da criação depende de
/// DocNumberSequenceService, que exige IDbConnection real.
/// </summary>
public class SalesContractAutoFixationTests
{
    private static SalesContract NewFixedContract() => new()
    {
        Key = Guid.NewGuid(),
        Code = "CV-FIXO",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "TN",
        HarvestSeasonCode = "24/25",
        TotalVolume = 1_500m,
        Price = 128.5m,
        FreightCostStandard = 45m,
        FreightTerms = FreightTerms.Cif,
        Type = ContractType.Fixed,
        Status = ContractStatus.Draft,
    };

    [Fact]
    public void BuildAutoFixation_CarriesTheNegotiatedFreight()
    {
        // Sem isto a fixação nasce com FreightCost = 0 e o relatório SalesPriceFixation.frx,
        // que imprime esse campo, mostra zero num contrato que tem frete acordado.
        var fixation = SalesContractsCreateService.BuildAutoFixation(NewFixedContract());

        Assert.Equal(45m, fixation.FreightCost);
    }

    [Fact]
    public void BuildAutoFixation_MirrorsVolumeAndPriceAndIsBornConfirmed()
    {
        var contract = NewFixedContract();

        var fixation = SalesContractsCreateService.BuildAutoFixation(contract);

        Assert.Same(contract, fixation.SalesContract);
        Assert.Equal(contract.TotalVolume, fixation.FixationVolume);
        Assert.Equal(contract.Price, fixation.FixationPrice);
        // Confirmed, não InApproval: TotalPrice conta só Confirmed e a fila da diretoria
        // é só para PAF.
        Assert.Equal(PriceFixationStatus.Confirmed, fixation.Status);
    }

    [Fact]
    public void BuildAutoFixation_WithoutFreight_KeepsItZero()
    {
        var contract = NewFixedContract();
        contract.FreightTerms = FreightTerms.None;
        contract.FreightCostStandard = 0m;

        var fixation = SalesContractsCreateService.BuildAutoFixation(contract);

        Assert.Equal(0m, fixation.FreightCost);
    }
}
