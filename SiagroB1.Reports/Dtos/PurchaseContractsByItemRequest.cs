namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros do relatório de contratos de compra por produto e período.
/// Só o período de emissão (FromDate/ToDate) é obrigatório; os demais campos,
/// quando nulos ou vazios, não restringem o resultado.
/// </summary>
public class PurchaseContractsByItemRequest
{
    /// <summary>Início do período de EMISSÃO do contrato (PurchaseContract.CreationDate).</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Fim do período de emissão, inclusivo até o fim do dia.</summary>
    public DateTime ToDate { get; set; }

    public string? ItemCode { get; set; }

    public string? HarvestSeasonCode { get; set; }

    public string? BranchCode { get; set; }

    public string? DeliveryLocationCode { get; set; }

    /// <summary>Fornecedor.</summary>
    public string? CardCode { get; set; }

    /// <summary>Início do período de ENTREGA. Filtra por sobreposição de janela.</summary>
    public DateTime? DeliveryFromDate { get; set; }

    public DateTime? DeliveryToDate { get; set; }
}
