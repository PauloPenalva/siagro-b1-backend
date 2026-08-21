using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Linha do histórico de movimentação da carga.
/// </summary>
/// <remarks>
/// <b>SHIPMENT_LOAD_MOVEMENTS É NARRATIVA, NÃO AUTORIDADE.</b>
/// <see cref="ShipmentLoad.InvoicedQuantity"/> é recalculado EXCLUSIVAMENTE de
/// <c>SALES_INVOICES</c>/<c>SALES_INVOICES_ITEMS</c>. Nenhum código soma esta tabela para
/// obter saldo: <see cref="Quantity"/> e <see cref="BalanceAfter"/> são snapshots gravados
/// DEPOIS do recálculo e nunca lidos de volta.
/// <para>
/// <see cref="SalesInvoiceKey"/> não tem FK nem navegação de propósito:
/// <c>SalesInvoicesDeleteService</c> apaga a nota pendente e todas as FKs do projeto são
/// <c>NoAction</c> — com FK real o delete quebraria. A linha sobrevive pelo
/// <see cref="InvoiceNumber"/> desnormalizado, que é o que o usuário lê. Mesmo precedente de
/// <c>SalesContractAllocation.CounterpartySalesContractKey</c>.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOAD_MOVEMENTS")]
[Index(nameof(ShipmentLoadKey))]
public class ShipmentLoadMovement : BaseEntity
{
    public required Guid ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public ShipmentLoadMovementType MovementType { get; set; }

    /// <summary>
    /// Quantidade ASSINADA: consumo negativo, devolução positiva. Zero nos movimentos que
    /// não mexem no saldo (montagem, devolução criada).
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Snapshot de <see cref="ShipmentLoad.AvailableQuantity"/> DEPOIS do recálculo.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal BalanceAfter { get; set; }

    public Guid? SalesInvoiceKey { get; set; }

    [Column(TypeName = "VARCHAR(9)")]
    public string? InvoiceNumber { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Description { get; set; }
}
