using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Fiscal.Tests.Payments;

/// <summary>
/// Condição de pagamento do tipo "Dias" (SE4 tipo 1 do Protheus): parcelas iguais, o resto na
/// última, contagem a partir da emissão ou do 1º dia do mês seguinte. Alimenta o cobr/dup e o
/// pag/detPag da NF-e.
/// </summary>
public class PaymentInstallmentCalculatorTests
{
    private static readonly DateOnly Issue = new(2026, 10, 2);

    [Fact]
    public void Zero_days_is_one_cash_installment_on_the_issue_date()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "15", 1000m, Issue);

        var installment = Assert.Single(plan.Installments);
        Assert.Equal(new PaymentInstallment(1, Issue, 1000m), installment);
        Assert.Equal(0, plan.PaymentIndicator);
        Assert.Equal(1000m, plan.PaidAmount);
    }

    [Fact]
    public void One_day_is_still_cash()
    {
        var plan = PaymentInstallmentCalculator.Calculate("1", PaymentStartRule.IssueDate, "17", 500m, Issue);

        Assert.Equal(new DateOnly(2026, 10, 3), plan.Installments.Single().DueDate);
        Assert.Equal(0, plan.PaymentIndicator);
    }

    [Fact]
    public void Thirty_days_is_term()
    {
        var plan = PaymentInstallmentCalculator.Calculate("30", PaymentStartRule.IssueDate, "15", 500m, Issue);

        Assert.Equal(new DateOnly(2026, 11, 1), plan.Installments.Single().DueDate);
        Assert.Equal(1, plan.PaymentIndicator);
    }

    [Fact]
    public void Remainder_goes_to_the_last_installment()
    {
        var plan = PaymentInstallmentCalculator.Calculate("30,60,90", PaymentStartRule.IssueDate, "15", 1000m, Issue);

        Assert.Equal(
            [
                new PaymentInstallment(1, new DateOnly(2026, 11, 1), 333.33m),
                new PaymentInstallment(2, new DateOnly(2026, 12, 1), 333.33m),
                new PaymentInstallment(3, new DateOnly(2026, 12, 31), 333.34m),
            ],
            plan.Installments);
        Assert.Equal(1, plan.PaymentIndicator);
    }

    [Fact]
    public void Next_month_counts_from_the_first_day_of_the_following_month()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0,30", PaymentStartRule.NextMonth, "15", 100m,
            new DateOnly(2026, 10, 15));

        Assert.Equal([new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)],
            plan.Installments.Select(i => i.DueDate));
        Assert.Equal([50m, 50m], plan.Installments.Select(i => i.Amount));
    }

    [Fact]
    public void Next_month_single_installment_is_term()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.NextMonth, "15", 100m,
            new DateOnly(2026, 10, 15));

        Assert.Equal(1, plan.PaymentIndicator);
    }

    [Fact]
    public void No_payment_means_has_no_installments_and_zero_paid()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "90", 1000m, Issue);

        Assert.Empty(plan.Installments);
        Assert.Null(plan.PaymentIndicator);
        Assert.Equal(0m, plan.PaidAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("30,abc")]
    [InlineData("60,30")]
    [InlineData("30,30")]
    [InlineData("-1")]
    [InlineData("30;60")]
    [InlineData("1.5")]
    [InlineData("1000")]
    public void Invalid_days_are_rejected(string days)
    {
        Assert.Throws<DefaultException>(() =>
            PaymentInstallmentCalculator.Calculate(days, PaymentStartRule.IssueDate, "15", 100m, Issue));
    }

    [Fact]
    public void Parse_days_accepts_spaces_around_commas()
    {
        Assert.Equal([30, 60, 90], PaymentInstallmentCalculator.ParseDays(" 30, 60 ,90 "));
    }

    [Fact]
    public void Unsupported_payment_means_is_rejected()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "05", 100m, Issue));

        Assert.Contains("05", ex.Message);
    }

    [Fact]
    public void Day_above_999_is_rejected_with_a_business_message()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            PaymentInstallmentCalculator.ParseDays("30,1000"));

        Assert.Contains("0 a 999", ex.Message);
    }
}
