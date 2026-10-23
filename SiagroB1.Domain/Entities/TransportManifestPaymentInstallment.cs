using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Parcela do pagamento a prazo (infPrazo).</summary>
[Table("TRANSPORT_MANIFEST_PAYMENT_INSTALLMENTS")]
public class TransportManifestPaymentInstallment
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid PaymentKey { get; set; }
    public virtual TransportManifestPayment? Payment { get; set; }

    public int Number { get; set; }
    public DateOnly DueDate { get; set; }
    [Column(TypeName = "DECIMAL(15,2)")] public decimal Value { get; set; }
}
