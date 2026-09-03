using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do diálogo de retorno de um documento de saída LEGADO: um romaneio da nota que ainda
/// pode ser devolvido.
/// </summary>
/// <remarks>
/// <b>Aqui a unidade de escolha é o ROMANEIO, e não a quantidade</b> — diferença deliberada em
/// relação a <see cref="ShipmentLoadRefusableDocumentDto"/>. Cada romaneio é uma carreta, e meia
/// carreta não volta do pátio: o parcial se expressa escolhendo quais carretas retornaram, e o
/// volume devolvido é a soma do <see cref="NetWeight"/> delas.
/// <para>
/// <see cref="WarehouseCode"/> vai junto porque é de lá que o grão saiu, e é a informação que o
/// operador usa para decidir o destino da devolução.
/// </para>
/// <para>
/// Todas as propriedades carregam <c>[JsonPropertyName]</c> em PascalCase: sem isso a resposta
/// sai em camelCase e o binding do UI5 encontra tudo <c>undefined</c>, sem erro nenhum.
/// </para>
/// </remarks>
public class SalesInvoiceReturnableShipmentDto
{
    [JsonPropertyName("StorageTransactionKey")]
    public required string StorageTransactionKey { get; set; }

    [JsonPropertyName("Code")]
    public string? Code { get; set; }

    [JsonPropertyName("TransactionDate")]
    public DateTime? TransactionDate { get; set; }

    [JsonPropertyName("TruckCode")]
    public string? TruckCode { get; set; }

    [JsonPropertyName("ItemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>Armazém de onde o grão saiu.</summary>
    [JsonPropertyName("WarehouseCode")]
    public string? WarehouseCode { get; set; }

    [JsonPropertyName("WarehouseName")]
    public string? WarehouseName { get; set; }

    [JsonPropertyName("GrossWeight")]
    public decimal GrossWeight { get; set; }

    /// <summary>O volume que este romaneio devolve — é a base da quantidade do retorno.</summary>
    [JsonPropertyName("NetWeight")]
    public decimal NetWeight { get; set; }
}
