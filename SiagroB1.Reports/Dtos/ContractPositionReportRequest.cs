using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros comuns aos relatórios de posição de contratos. Não há período: o relatório é a posição
/// no momento da emissão. Tudo é opcional; Situação vazia = só Aprovado.
/// </summary>
public abstract class ContractPositionReportRequest
{
    public List<ContractStatus>? Statuses { get; set; }

    public string? BranchCode { get; set; }

    public string? ItemCode { get; set; }

    public string? HarvestSeasonCode { get; set; }

    /// <summary>Parceiro: fornecedor no contrato de compra, cliente no de venda.</summary>
    public string? CardCode { get; set; }

    /// <summary>FIX (Fixed) ou PAF (ToBeDetermined); vazio = os dois.</summary>
    public ContractType? Type { get; set; }

    /// <summary>Término da entrega até (inclusive). Contrato sem prazo não passa neste filtro.</summary>
    public DateTime? DeliveryEndDateUntil { get; set; }
}
