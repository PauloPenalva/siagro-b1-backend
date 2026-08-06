namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Rascunho do documento de entrada lido do XML — ainda NÃO gravado. Quem grava é o POST do
/// documento; a importação só interpreta o arquivo para o operador não redigitar.
///
/// É DTO, e não objeto anônimo: o anônimo sai do serializador padrão em camelCase, fora do EDM, e
/// o cliente (que lê PascalCase como todo o resto da API) recebe tudo indefinido sem erro nenhum.
/// </summary>
public class PurchaseInvoiceDraftDto
{
    public string? CardCode { get; set; }

    public string? CardName { get; set; }

    public string? TaxDocumentNumber { get; set; }

    public string? TaxDocumentSeries { get; set; }

    public string? ChaveNFe { get; set; }

    public DateTime? IssueDate { get; set; }

    /// <summary>Total declarado pelo emitente (<c>ICMSTot/vNF</c>), não a soma das linhas.</summary>
    public decimal TotalDocumentValue { get; set; }

    public string? TaxPayerComments { get; set; }

    public string? XmlFileName { get; set; }

    public List<PurchaseInvoiceDraftItemDto> Items { get; set; } = [];
}

public class PurchaseInvoiceDraftItemDto
{
    public string? ItemCode { get; set; }

    public string? ItemName { get; set; }

    public string? UnitOfMeasureCode { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
