using SiagroB1.Fiscal.Taxes;

namespace SiagroB1.Fiscal.Tests.Taxes;

/// <summary>Texto do Funrural no infCpl (spec 2026-10-09 §6).</summary>
public class FunruralTextTests
{
    private static FunruralTextLine Pf(decimal inss, decimal rat, decimal senar) => new(1.32m, 0.11m, 0.20m, inss, rat, senar);

    [Fact]
    public void Individual_producer_lists_the_three_parts()
    {
        Assert.Equal(
            "Contribuição Social denominada Funrural 1,63% = R$ 24,45 (INSS 1,32% R$ 19,80; RAT 0,11% R$ 1,65; SENAR 0,20% R$ 3,00)",
            FunruralText.Build([Pf(19.80m, 1.65m, 3.00m)], isReturn: false));
    }

    [Fact]
    public void Values_use_the_brazilian_thousand_separator()
    {
        Assert.Equal(
            "Contribuição Social denominada Funrural 1,63% = R$ 1.630,00 (INSS 1,32% R$ 1.320,00; RAT 0,11% R$ 110,00; SENAR 0,20% R$ 200,00)",
            FunruralText.Build([Pf(1320m, 110m, 200m)], isReturn: false));
    }

    [Fact]
    public void Zero_parts_are_left_out()
    {
        Assert.Equal(
            "Contribuição Social denominada Funrural 2,23% = R$ 33,45 (INSS 1,98% R$ 29,70; SENAR 0,25% R$ 3,75)",
            FunruralText.Build([new FunruralTextLine(1.98m, 0m, 0.25m, 29.70m, 0m, 3.75m)], isReturn: false));
    }

    [Fact]
    public void Senar_only_has_its_own_sentence()
    {
        Assert.Equal("SENAR 0,20% = R$ 3,00",
            FunruralText.Build([new FunruralTextLine(0m, 0m, 0.20m, 0m, 0m, 3.00m)], isReturn: false));
    }

    [Fact]
    public void Lines_with_the_same_rates_are_summed()
    {
        Assert.Equal(
            "Contribuição Social denominada Funrural 1,63% = R$ 36,68 (INSS 1,32% R$ 29,70; RAT 0,11% R$ 2,48; SENAR 0,20% R$ 4,50)",
            FunruralText.Build([Pf(19.80m, 1.65m, 3.00m), Pf(9.90m, 0.83m, 1.50m)], isReturn: false));
    }

    [Fact]
    public void Different_rates_become_separate_sentences()
    {
        Assert.Equal(
            "Contribuição Social denominada Funrural 1,63% = R$ 24,45 (INSS 1,32% R$ 19,80; RAT 0,11% R$ 1,65; SENAR 0,20% R$ 3,00)" +
            " | SENAR 0,20% = R$ 1,00",
            FunruralText.Build([Pf(19.80m, 1.65m, 3.00m), new FunruralTextLine(0m, 0m, 0.20m, 0m, 0m, 1.00m)], isReturn: false));
    }

    [Fact]
    public void Return_is_prefixed_as_reversal()
    {
        Assert.Equal("Estorno - SENAR 0,20% = R$ 3,00",
            FunruralText.Build([new FunruralTextLine(0m, 0m, 0.20m, 0m, 0m, 3.00m)], isReturn: true));
    }

    [Fact]
    public void Nothing_withheld_has_no_text()
    {
        Assert.Null(FunruralText.Build([new FunruralTextLine(0m, 0m, 0m, 0m, 0m, 0m)], isReturn: false));
        Assert.Null(FunruralText.Build([], isReturn: false));
    }
}
