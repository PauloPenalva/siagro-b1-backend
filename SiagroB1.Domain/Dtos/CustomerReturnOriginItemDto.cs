using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha de saída oferecida como origem de uma devolução do cliente, já com a quebra apurada
/// calculada — é o número contra o qual a quantidade devolvida é conferida.
/// </summary>
public class CustomerReturnOriginItemDto
{
    [Key]
    public Guid SalesInvoiceItemKey { get; set; }

    public string? InvoiceNumber { get; set; }

    /// <summary>Número da NF-e da saída: é por ele que o cliente referencia a origem.</summary>
    public string? TaxDocumentNumber { get; set; }

    public DateTime? InvoiceDate { get; set; }

    public string? ItemCode { get; set; }

    public string? ItemName { get; set; }

    public string? UnitOfMeasureCode { get; set; }

    public decimal Quantity { get; set; }

    public decimal DeliveredQuantity { get; set; }

    public decimal QuantityLoss { get; set; }

    public decimal AssessedShortage { get; set; }

    public string? SalesContractCode { get; set; }
}
