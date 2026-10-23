using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Componente do pagamento (infPag/Comp): tpComp 01 vale-pedágio, 02 impostos, 03 despesas, 99 outros.</summary>
[Table("TRANSPORT_MANIFEST_PAYMENT_COMPONENTS")]
public class TransportManifestPaymentComponent
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid PaymentKey { get; set; }
    public virtual TransportManifestPayment? Payment { get; set; }

    [Column(TypeName = "VARCHAR(2)")] public required string Type { get; set; }
    [Column(TypeName = "DECIMAL(15,2)")] public decimal Value { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? Description { get; set; }
}
