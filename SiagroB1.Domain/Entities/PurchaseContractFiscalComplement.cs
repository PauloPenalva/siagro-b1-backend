using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Complemento FISCAL do contrato de compra (spec 2026-10-13): a natureza de operação, que dá as alíquotas de
/// Funrural do totalizador e (sub-projeto 2) da retenção no pagamento ao produtor. Separado do contrato porque
/// quem alimenta é o fiscal, com permissão própria (<c>PURCHASE_CONTRACT_FISCAL_EDIT</c>) — molde de
/// <see cref="SalesContractFiscalComplement"/>.
/// <para>Sem FK para a natureza: em SAPB1 ela vem do <c>OUSG</c>. Só usado em STANDALONE.</para>
/// </summary>
[Table("PURCHASE_CONTRACT_FISCAL_COMPLEMENTS")]
public class PurchaseContractFiscalComplement
{
    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public int? UsageCode { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
