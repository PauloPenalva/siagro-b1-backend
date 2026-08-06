using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha de saída oferecida como ORIGEM de uma devolução, já com a quebra apurada calculada — é o
/// número contra o qual a quantidade devolvida é conferida.
///
/// Alimenta o value help da amarração no documento de entrada tipo <c>Return</c>.
/// </summary>
public class PurchaseInvoiceOriginItemDto
{
    /// <summary>Chave do EDM: sem ela a coleção não é endereçável.</summary>
    [Key]
    public Guid SalesInvoiceItemKey { get; set; }

    public string? InvoiceNumber { get; set; }

    /// <summary>Número da NF-e da saída: é por ele que o emitente referencia a origem.</summary>
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
