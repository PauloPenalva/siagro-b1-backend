namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Rascunho da devolução lido do XML — ainda não gravado.
///
/// É DTO, e não objeto anônimo: o anônimo sai do serializador padrão em camelCase, fora do
/// EDM, e o cliente (que lê PascalCase como todo o resto da API) recebe tudo indefinido sem
/// erro nenhum.
/// </summary>
public class CustomerReturnDraftDto
{
    public string? CardCode { get; set; }

    public string? CardName { get; set; }

    public string? DocumentNumber { get; set; }

    public string? DocumentSeries { get; set; }

    public string? AccessKey { get; set; }

    public DateTime? IssueDate { get; set; }

    public decimal TotalValue { get; set; }

    public string? TaxPayerComments { get; set; }

    public string? XmlFileName { get; set; }

    public List<CustomerReturnDraftItemDto> Items { get; set; } = [];
}

public class CustomerReturnDraftItemDto
{
    public string? ItemCode { get; set; }

    public string? ItemName { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
