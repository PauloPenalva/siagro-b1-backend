using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services;

/// <summary>
/// Frete, seguro, desconto e outras despesas da linha do documento de saída e do de entrada (spec 2026-10-05 §5, R2).
/// Valem em TODAS as filiais (R1): a regra é de gravação, não de tributação. Mora num lugar só porque são seis os
/// caminhos que gravam linha — inclusão e alteração do documento e da linha, nas duas entidades — e a grade da tela
/// alcança cada um por uma ação diferente.
/// </summary>
public static class InvoiceLineChargeRules
{
    /// <summary>
    /// Arredonda os quatro valores em centavos — a coluna é DECIMAL(18,2), e o cálculo dos tributos não pode ver uma casa
    /// que o banco descartaria — e recusa valor negativo ou desconto acima de itens + frete + seguro + outras despesas.
    /// Desconto IGUAL ao valor da linha passa (bonificação): o total geral fica 0.
    /// </summary>
    public static void Ensure(INfeTaxedLine line)
    {
        line.FreightValue = Cents(line.FreightValue);
        line.InsuranceValue = Cents(line.InsuranceValue);
        line.DiscountValue = Cents(line.DiscountValue);
        line.OtherExpensesValue = Cents(line.OtherExpensesValue);

        if (line.FreightValue < 0 || line.InsuranceValue < 0 || line.DiscountValue < 0 || line.OtherExpensesValue < 0)
            throw new DefaultException(
                $"Item {line.ItemCode}: frete, seguro, desconto e outras despesas não podem ser negativos.");

        // Math.Max: quantidade negativa deixa Total negativo, e o teto não pode recusar um desconto 0 com a mensagem do
        // desconto — a quantidade inválida tem mensagem própria, dada pelo serviço que a conhece.
        var ceiling = Math.Max(0m, line.Total + line.FreightValue + line.InsuranceValue + line.OtherExpensesValue);
        if (line.DiscountValue > ceiling)
            throw new DefaultException($"Item {line.ItemCode}: o desconto passa do valor da linha.");
    }

    /// <summary>
    /// Valor da linha de origem na proporção do que volta (spec 2026-10-05 D4): valor × devolvida ÷ original, em centavos
    /// (<see cref="MidpointRounding.AwayFromZero"/>). Devolvendo a quantidade inteira o valor vem exato; a soma de várias
    /// devoluções parciais pode diferir 1 centavo da origem (risco aceito, a última não é ajustada). Origem sem quantidade:
    /// nada volta.
    /// </summary>
    public static decimal Proportional(decimal value, decimal returnedQuantity, decimal originalQuantity) =>
        originalQuantity == 0m
            ? 0m
            : decimal.Round(value * returnedQuantity / originalQuantity, 2, MidpointRounding.AwayFromZero);

    private static decimal Cents(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
