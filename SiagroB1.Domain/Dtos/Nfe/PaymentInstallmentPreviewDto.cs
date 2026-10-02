namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>Uma parcela da prévia da tela de condições de pagamento.</summary>
public class PaymentInstallmentPreviewDto
{
    public int Number { get; set; }

    public DateOnly DueDate { get; set; }

    public decimal Amount { get; set; }
}
