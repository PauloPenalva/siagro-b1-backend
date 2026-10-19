using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Parcelas de um documento pela condição de pagamento (outra compra e saída sem contrato).</summary>
internal static class PaymentInstallmentPlanner
{
    /// <summary>
    /// Parcelas pela condição; sem condição, ou sem parcelas (meio 90), é 1 título vencendo na emissão. <paramref name="net"/>
    /// é o valor líquido a parcelar (quem chama já descontou retenções, se houver).
    /// </summary>
    public static async Task<List<(int Number, DateTime DueDate, decimal Amount)>> PlanAsync(
        AppDbContext context, int? paymentConditionCode, decimal net, DateTime issueDate)
    {
        if (paymentConditionCode is { } code)
        {
            var condition = await context.PaymentConditions.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code)
                            ?? throw new DefaultException(
                                $"A condição de pagamento {code} do documento não existe mais: escolha outra antes de confirmar.");
            var plan = PaymentInstallmentCalculator.Calculate(
                condition.Days, condition.StartRule, condition.PaymentMeans, net, DateOnly.FromDateTime(issueDate));
            if (plan.Installments.Count > 0)
                return plan.Installments
                    .Select(i => (i.Number, i.DueDate.ToDateTime(TimeOnly.MinValue), decimal.Round(i.Amount, 2, MidpointRounding.ToEven)))
                    .ToList();
        }

        return [(1, issueDate, net)];
    }
}
