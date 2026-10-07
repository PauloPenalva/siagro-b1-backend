using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Complemento FISCAL do contrato de venda (spec 2026-10-07): a instrução de faturamento do cliente — natureza de
/// operação, condição de pagamento, informações adicionais e pedido do cliente. Separado do contrato para não
/// misturar o fiscal com o comercial e para ter permissão própria de edição (<c>SALES_CONTRACT_FISCAL_EDIT</c>).
/// <para>
/// ⚠️ Não confundir com <see cref="SalesContract.Complement"/>, que é um rótulo comercial.
/// Sem FK para natureza e condição: em SAPB1 a natureza vem do <c>OUSG</c> (mesmo motivo do <see cref="ItemComplement"/>).
/// </para>
/// </summary>
[Table("SALES_CONTRACT_FISCAL_COMPLEMENTS")]
public class SalesContractFiscalComplement
{
    public Guid SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    public int? UsageCode { get; set; }

    public int? PaymentConditionCode { get; set; }

    [Column(TypeName = "VARCHAR(2000)")]
    public string? AdditionalInfo { get; set; }

    /// <summary>Pedido de compra do cliente (<c>det/prod/xPed</c>), até 15 caracteres.</summary>
    [Column(TypeName = "VARCHAR(15)")]
    public string? CustomerOrderNumber { get; set; }

    /// <summary>Item do pedido do cliente (<c>det/prod/nItemPed</c>), 1 a 6 dígitos.</summary>
    [Column(TypeName = "VARCHAR(6)")]
    public string? CustomerOrderItem { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
