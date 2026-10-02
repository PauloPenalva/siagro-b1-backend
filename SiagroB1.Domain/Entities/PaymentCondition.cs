using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Condição de pagamento do tipo "Dias" (inspirada no SE4 do Protheus). Só STANDALONE. Os
/// percentuais e a condição "informada no documento" ficaram fora do sub-projeto 2a.
/// </summary>
[Table("PAYMENT_CONDITIONS")]
public class PaymentCondition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Dias separados por vírgula, crescentes: <c>0</c>, <c>30</c>, <c>30,60,90</c>.</summary>
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Days { get; set; }

    public PaymentStartRule StartRule { get; set; } = PaymentStartRule.IssueDate;

    /// <summary>Meio de pagamento da SEFAZ (<c>tPag</c>): 15 boleto, 17 PIX, 90 sem pagamento...</summary>
    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public required string PaymentMeans { get; set; }

    public bool Inactive { get; set; }
}
