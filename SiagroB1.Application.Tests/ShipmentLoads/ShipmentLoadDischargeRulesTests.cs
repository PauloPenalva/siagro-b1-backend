using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (rateio): a soma das parcelas fecha com o peso do ticket, em 3 casas, com tolerância de
/// 0,001. É a mesma regra da tela — o servidor confere de novo porque nada impede a tela de mandar
/// outra coisa.
/// </summary>
public class ShipmentLoadDischargeRulesTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Drops_the_zero_shares_and_keeps_the_order()
    {
        var lines = ShipmentLoadDischargeRules.NormalizeDistribution(
            35_000m, [new(A, 20_000m), new(Guid.NewGuid(), 0m), new(B, 15_000m)]);

        Assert.Equal(new[] { A, B }, lines.Select(l => l.SalesInvoiceItemKey));
    }

    [Fact]
    public void Rounds_each_share_to_three_decimals()
    {
        var lines = ShipmentLoadDischargeRules.NormalizeDistribution(
            1_000m, [new(A, 333.3334m), new(B, 666.6666m)]);

        Assert.Equal(new[] { 333.333m, 666.667m }, lines.Select(l => l.Quantity));
    }

    /// <summary>Review Focus 3: ruído de ponto flutuante vindo da tela não é erro do usuário.</summary>
    [Fact]
    public void Float_noise_within_the_tolerance_is_accepted()
    {
        var lines = ShipmentLoadDischargeRules.NormalizeDistribution(
            35_000.0004m, [new(A, 20_000m), new(B, 15_000m)]);

        Assert.Equal(2, lines.Count);
    }

    /// <summary>Review Focus 3: a mensagem nomeia os dois números, em pt-BR.</summary>
    [Fact]
    public void A_distribution_that_does_not_close_is_refused_naming_both_numbers()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.NormalizeDistribution(
            35_000m, [new(A, 20_000m), new(B, 5_000m)]));

        Assert.Equal("O rateio (25.000,000) não fecha com o peso descarregado (35.000,000).", ex.Message);
    }

    [Fact]
    public void A_negative_share_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.NormalizeDistribution(
            10_000m, [new(A, 12_000m), new(B, -2_000m)]));

        Assert.Equal("O peso rateado não pode ser negativo.", ex.Message);
    }

    [Fact]
    public void A_repeated_item_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.NormalizeDistribution(
            10_000m, [new(A, 5_000m), new(A, 5_000m)]));

        Assert.Equal("O mesmo item de documento de saída aparece duas vezes no rateio.", ex.Message);
    }

    [Fact]
    public void Nothing_distributed_is_refused()
    {
        const string message = "Distribua o peso descarregado entre os documentos de saída.";

        Assert.Equal(message, Assert.Throws<DefaultException>(
            () => ShipmentLoadDischargeRules.NormalizeDistribution(10_000m, [new(A, 0m)])).Message);
        Assert.Equal(message, Assert.Throws<DefaultException>(
            () => ShipmentLoadDischargeRules.NormalizeDistribution(10_000m, [])).Message);
        Assert.Equal(message, Assert.Throws<DefaultException>(
            () => ShipmentLoadDischargeRules.NormalizeDistribution(10_000m, null)).Message);
    }

    [Fact]
    public void Rounds_the_ticket_weight_to_three_decimals()
    {
        Assert.Equal(39_500.000m, ShipmentLoadDischargeRules.RoundQuantity(39_500.0004m));
    }

    [Fact]
    public void Change_log_describes_the_distribution()
    {
        Assert.Equal(
            "T-1 — 35.000,000 (000100: 20.000,000; (sem número): 15.000,000)",
            ShipmentLoadChangeLogFields.DescribeDischarge("T-1", 35_000m, [("000100", 20_000m), (null, 15_000m)]));

        Assert.Equal("T-1 — 35.000,000", ShipmentLoadChangeLogFields.DescribeDischarge("T-1", 35_000m, []));
    }
}
