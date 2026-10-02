using System.Globalization;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Fiscal.Payments;

public sealed record PaymentInstallment(int Number, DateOnly DueDate, decimal Amount);

/// <param name="PaymentIndicator"><c>indPag</c>: 0 à vista, 1 a prazo, nulo sem pagamento.</param>
/// <param name="PaidAmount"><c>vPag</c>: o total, ou 0 com o meio "sem pagamento".</param>
public sealed record PaymentPlan(
    string PaymentMeans,
    int? PaymentIndicator,
    decimal PaidAmount,
    IReadOnlyList<PaymentInstallment> Installments);

/// <summary>Meios de pagamento (<c>tPag</c>) que o cadastro aceita — os da SEFAZ que fazem sentido aqui.</summary>
public static class PaymentMeansCodes
{
    public const string NoPayment = "90";
    public const string Other = "99";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["01"] = "Dinheiro",
        ["03"] = "Cartão de crédito",
        ["04"] = "Cartão de débito",
        ["15"] = "Boleto bancário",
        ["16"] = "Depósito bancário",
        ["17"] = "PIX",
        ["18"] = "Transferência bancária",
        ["90"] = "Sem pagamento",
        ["99"] = "Outros",
    };
}

/// <summary>
/// Parcelas da condição "Dias" (SE4 tipo 1 do Protheus): valor igual em cada parcela,
/// arredondado em 2 casas, e o resto na última — a soma fecha sempre no total.
/// </summary>
public static class PaymentInstallmentCalculator
{
    /// <summary>O grupo <c>dup</c> da NF-e aceita até 120 ocorrências.</summary>
    public const int MaxInstallments = 120;

    public static IReadOnlyList<int> ParseDays(string? days)
    {
        if (string.IsNullOrWhiteSpace(days))
            throw new DefaultException("Informe os dias da condição de pagamento (ex.: 0 ou 30,60,90).");

        var result = new List<int>();

        foreach (var part in days.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var day))
                throw new DefaultException(
                    $"Dia inválido na condição de pagamento: \"{part}\". Use números inteiros separados por vírgula.");

            if (result.Count > 0 && day <= result[^1])
                throw new DefaultException("Os dias da condição de pagamento devem ser crescentes (ex.: 30,60,90).");

            result.Add(day);
        }

        if (result.Count > MaxInstallments)
            throw new DefaultException($"A condição de pagamento aceita no máximo {MaxInstallments} parcelas.");

        return result;
    }

    public static PaymentPlan Calculate(
        string days, PaymentStartRule startRule, string paymentMeans, decimal total, DateOnly issueDate)
    {
        if (!PaymentMeansCodes.All.ContainsKey(paymentMeans))
            throw new DefaultException($"Meio de pagamento {paymentMeans} não é aceito pela condição de pagamento.");

        if (paymentMeans == PaymentMeansCodes.NoPayment)
            return new PaymentPlan(paymentMeans, null, 0m, []);

        var dayList = ParseDays(days);
        var start = startRule == PaymentStartRule.NextMonth
            ? new DateOnly(issueDate.Year, issueDate.Month, 1).AddMonths(1)
            : issueDate;

        var count = dayList.Count;
        var share = decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var installments = new List<PaymentInstallment>(count);

        for (var i = 0; i < count; i++)
        {
            var amount = i == count - 1 ? total - share * (count - 1) : share;
            installments.Add(new PaymentInstallment(i + 1, start.AddDays(dayList[i]), amount));
        }

        // À vista: uma parcela vencendo até o dia seguinte à emissão.
        var cash = count == 1 && installments[0].DueDate <= issueDate.AddDays(1);

        return new PaymentPlan(paymentMeans, cash ? 0 : 1, total, installments);
    }
}
