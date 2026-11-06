using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

/// <summary>
/// Regras e normalização de uma linha da tabela de preços. Compartilhadas pela gravação avulsa da linha e pela
/// inclusão em profundidade (cabeçalho + linhas), para que as duas vias nunca divirjam.
/// </summary>
public static class MarketPriceTableLineRules
{
    public static async Task ValidateAsync(AppDbContext context, MarketPriceTableLine entity)
    {
        entity.Description = entity.Description?.Trim() ?? string.Empty;
        if (entity.Description.Length == 0)
            throw new BusinessException("Informe a descrição da linha.");
        if (entity.Description.Length > 100)
            throw new BusinessException("A descrição da linha deve ter no máximo 100 caracteres.");

        var instrument = await context.MarketInstruments.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Code == entity.InstrumentCode)
            ?? throw new BusinessException("Instrumento não encontrado.");
        if (!instrument.Active)
            throw new BusinessException("O instrumento da linha está inativo.");
        if (instrument.Type is not (MarketInstrumentType.Future or MarketInstrumentType.Spot)
            || instrument.QuoteUnit == MarketQuoteUnit.BrlPerUsd)
            throw new BusinessException("O instrumento da linha deve ser um contrato futuro ou spot de mercadoria.");

        var destination = await context.MarketDestinations.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == entity.DestinationId)
            ?? throw new BusinessException("Destino não encontrado.");
        if (!destination.Active)
            throw new BusinessException("O destino da linha está inativo.");

        if (instrument.QuoteUnit is MarketQuoteUnit.UsCentsPerBushel or MarketQuoteUnit.UsdPerTon)
        {
            var curveCode = entity.FxCurveInstrumentCode?.Trim();
            if (string.IsNullOrEmpty(curveCode))
                throw new BusinessException("Instrumento cotado em dólar exige o ponto da curva de dólar.");
            var curve = await context.MarketInstruments.AsNoTracking().FirstOrDefaultAsync(i => i.Code == curveCode)
                        ?? throw new BusinessException("Ponto da curva de dólar não encontrado.");
            if (curve.Type != MarketInstrumentType.CurvePoint || curve.CurveDays is null or <= 0)
                throw new BusinessException("O ponto da curva de dólar deve ser um instrumento de curva com dias maiores que zero.");
            entity.FxCurveInstrumentCode = curve.Code;
        }
        else
        {
            entity.FxCurveInstrumentCode = null;
        }

        if (entity.PaymentDate is null)
            throw new BusinessException("Informe a data de pagamento.");
        if (entity.ReceiptDate is null)
            throw new BusinessException("Informe a data de recebimento.");
        entity.PaymentDate = entity.PaymentDate.Value.Date;
        entity.ReceiptDate = entity.ReceiptDate.Value.Date;
        entity.DeliveryStartDate = entity.DeliveryStartDate?.Date;
        entity.DeliveryEndDate = entity.DeliveryEndDate?.Date;
        if (entity.DeliveryStartDate > entity.DeliveryEndDate)
            throw new BusinessException("O início da entrega não pode ser posterior ao fim da entrega.");

        Percent(entity.MarginPercent, "margem");
        Percent(entity.DailyInterestPercent, "juros diários");
        Percent(entity.TechnicalLossPercent, "quebra técnica");
        Percent(entity.RuralTaxPercent, "imposto rural");
        Percent(entity.FreightCoefficientPercent, "coeficiente de frete");
        if (entity.DiscountIsPercent)
            Percent(entity.DiscountValue, "desconto");
        else if (entity.DiscountValue < 0)
            throw new BusinessException("O desconto não pode ser negativo.");
        if (entity.ElevationCost < 0)
            throw new BusinessException("O custo de elevação não pode ser negativo.");
    }

    private static void Percent(decimal value, string name)
    {
        if (value < 0 || value > 100)
            throw new BusinessException($"O percentual de {name} deve estar entre 0 e 100.");
    }
}
