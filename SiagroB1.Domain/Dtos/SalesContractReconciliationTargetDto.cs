using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do dialog de conciliação: um contrato de venda candidato a receber volume de
/// outro contrato. Ao contrário de <see cref="SalesShipmentReleaseAvailableDto"/>, NÃO
/// exige liberação de entrega e NÃO filtra por saldo — contratos esgotados ou já
/// negativos precisam aparecer, senão a conciliação continua travada. O
/// <see cref="Balance"/> vem calculado para a tela poder destacar os negativos.
/// </summary>
public class SalesContractReconciliationTargetDto
{
    [JsonPropertyName("SalesContractKey")]
    public required string SalesContractKey { get; set; }

    [JsonPropertyName("RowId")]
    public int RowId { get; set; }

    [JsonPropertyName("Code")]
    public string? Code { get; set; }

    [JsonPropertyName("Complement")]
    public string? Complement { get; set; }

    [JsonPropertyName("BranchShortName")]
    public string? BranchShortName { get; set; }

    [JsonPropertyName("CardCode")]
    public string? CardCode { get; set; }

    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }

    /// <summary>CNPJ/CPF do cliente, desnormalizado em SALES_CONTRACTS.</summary>
    [JsonPropertyName("CardTaxId")]
    public string? CardTaxId { get; set; }

    /// <summary>
    /// Contrato de cliente diferente do cliente da NOTA. Vem calculado do servidor porque
    /// o CardCode da nota não está no $select do binding da tela (autoExpandSelect só
    /// seleciona o que algum controle binda), e buscá-lo depois cairia em late property.
    /// </summary>
    [JsonPropertyName("IsOtherCustomer")]
    public bool IsOtherCustomer { get; set; }

    [JsonPropertyName("ItemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }

    [JsonPropertyName("HarvestSeasonCode")]
    public string? HarvestSeasonCode { get; set; }

    [JsonPropertyName("Price")]
    public decimal Price { get; set; }

    [JsonPropertyName("TotalVolume")]
    public decimal TotalVolume { get; set; }

    [JsonPropertyName("AllocatedVolume")]
    public decimal AllocatedVolume { get; set; }

    /// <summary>Saldo disponível por nota (TotalVolume − AllocatedVolume). Pode ser NEGATIVO.</summary>
    [JsonPropertyName("Balance")]
    public decimal Balance { get; set; }

    /// <summary>
    /// Soma do saldo das liberações de entrega ATIVAS do contrato. É o quanto o volume
    /// conciliado vai efetivamente consumir de liberação (FIFO por data); o que passar
    /// disso fica estacionado sem liberação. Zero em contrato legado, que nunca teve
    /// liberação — e continua sendo destino válido.
    /// </summary>
    [JsonPropertyName("ActiveReleaseBalance")]
    public decimal ActiveReleaseBalance { get; set; }

    /// <summary>Volume digitado na tela — não vem do servidor.</summary>
    [JsonPropertyName("ReconciliationVolume")]
    public decimal? ReconciliationVolume { get; set; }
}
