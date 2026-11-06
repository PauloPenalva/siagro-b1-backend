using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Market.Pricing;

/// <summary>Entradas da fórmula única de preço (R$/saca 60 kg) na origem.</summary>
public sealed record MarketPriceInputs(
    decimal? Quote, MarketQuoteUnit QuoteUnit, decimal TonFactor, decimal Premium, decimal ElevationCost,
    decimal? SpotFx, decimal? CurveFx, int? CurveDays, decimal FxDiscount,
    DateOnly Today, DateOnly ReceiptDate, DateOnly PaymentDate,
    decimal DiscountValue, bool DiscountIsPercent, decimal MarginPercent, decimal DailyInterestPercent,
    decimal TechnicalLossPercent, decimal RuralTaxPercent, decimal? FreightPerTon, decimal FreightCoefficientPercent);

/// <summary>Passo do cálculo; <c>Key</c> é estável (chave i18n no frontend).</summary>
public sealed record MarketPriceStep(string Key, decimal Value, string Unit);

public sealed record MarketPriceResult(MarketPriceStatus Status, decimal? Price, decimal? NetFx, decimal? PortPrice,
    decimal? FreightPerBag, IReadOnlyList<MarketPriceStep> Steps);

/// <summary>
/// Fórmula única de preço do módulo Mercado: pura, sem dependências e sem exceções.
/// Cotação + prêmio, conversão para R$/saca 60 kg (preço porto), depois descontos,
/// margem, juros, quebra técnica, frete e imposto rural.
/// </summary>
public static class MarketPriceCalculator
{
    public static MarketPriceResult Calculate(MarketPriceInputs input)
    {
        try { return CalculateCore(input); }
        catch (OverflowException) { return Fail(MarketPriceStatus.InvalidSetup, new List<MarketPriceStep>()); }
    }

    private static MarketPriceResult CalculateCore(MarketPriceInputs i)
    {
        var steps = new List<MarketPriceStep>();
        var isBrl = i.QuoteUnit == MarketQuoteUnit.BrlPerBag;

        // O próprio dólar não é um instrumento precificável; fator de tonelada inválido idem.
        if (i.QuoteUnit == MarketQuoteUnit.BrlPerUsd || (!isBrl && i.TonFactor <= 0))
            return Fail(MarketPriceStatus.InvalidSetup, steps);

        if (i.Quote is not > 0)
            return Fail(MarketPriceStatus.MissingQuote, steps);
        var quote = i.Quote.Value;

        var gross = quote + i.Premium;
        steps.Add(new("exchange", quote, "quote"));
        steps.Add(new("premium", i.Premium, "quote"));
        steps.Add(new("gross", gross, "quote"));

        var perTonUnit = isBrl ? "BRL/t" : "USD/t";
        if (!isBrl)
            steps.Add(new("perTon", gross * i.TonFactor, perTonUnit));
        steps.Add(new("elevation", i.ElevationCost, perTonUnit));

        decimal netFx = 1m;
        if (!isBrl)
        {
            if (i.SpotFx is null || i.CurveFx is null || i.CurveDays is not > 0)
                return Fail(MarketPriceStatus.MissingFx, steps);

            var curveDays = i.CurveDays.Value;
            var slope = Round(( i.CurveFx.Value - i.SpotFx.Value) / curveDays, 6);
            var daysToPayment = i.PaymentDate.DayNumber - i.Today.DayNumber;
            netFx = Round(i.CurveFx.Value + (daysToPayment - curveDays) * slope - i.FxDiscount, 4);
            steps.Add(new("netFx", netFx, "BRL/USD"));
        }

        var port = isBrl
            ? gross - i.ElevationCost * 0.06m
            : (gross * i.TonFactor - i.ElevationCost) * netFx * 60m / 1000m;
        steps.Add(new("portPrice", port, "BRL/sc"));

        var interest = Round(port * i.DailyInterestPercent / 100m * (i.PaymentDate.DayNumber - i.ReceiptDate.DayNumber), 2);

        var p = i.DiscountIsPercent ? port * (1 - i.DiscountValue / 100m) : port - i.DiscountValue;
        steps.Add(new("discount", port - p, "BRL/sc"));

        var afterMargin = p * (1 - i.MarginPercent / 100m);
        steps.Add(new("margin", p - afterMargin, "BRL/sc"));
        p = afterMargin;

        p += interest;
        steps.Add(new("interest", interest, "BRL/sc"));

        var afterLoss = p * (1 - i.TechnicalLossPercent / 100m);
        steps.Add(new("technicalLoss", p - afterLoss, "BRL/sc"));
        p = afterLoss;

        if (i.FreightPerTon is null)
            return new(MarketPriceStatus.MissingFreight, null, netFx, port, null, steps);

        var freightPerTon = i.FreightPerTon.Value;
        var freightPerBag = Round(
            (freightPerTon + Round(freightPerTon * i.FreightCoefficientPercent / 100m, 2)) * 60m / 1000m, 2);
        p -= freightPerBag;
        steps.Add(new("freight", freightPerBag, "BRL/sc"));

        var afterTax = p * (1 - i.RuralTaxPercent / 100m);
        steps.Add(new("ruralTax", p - afterTax, "BRL/sc"));

        var price = Round(afterTax, 2);
        steps.Add(new("price", price, "BRL/sc"));

        return new(MarketPriceStatus.Ok, price, netFx, port, freightPerBag, steps);
    }

    private static MarketPriceResult Fail(MarketPriceStatus status, List<MarketPriceStep> steps) =>
        new(status, null, null, null, null, steps);

    private static decimal Round(decimal v, int n) => Math.Round(v, n, MidpointRounding.AwayFromZero);
}
