namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros do relatório de contratos de venda por produto e período. Espelha
/// <see cref="PurchaseContractsByItemRequest"/>, trocando o local de entrega
/// (1:N na venda) pela região logística e o fornecedor pelo cliente.
/// Só o período de emissão é obrigatório.
/// </summary>
public class SalesContractsByItemRequest
{
    /// <summary>Início do período de EMISSÃO do contrato (SalesContract.CreationDate).</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Fim do período de emissão, inclusivo até o fim do dia.</summary>
    public DateTime ToDate { get; set; }

    public string? ItemCode { get; set; }

    public string? HarvestSeasonCode { get; set; }

    public string? BranchCode { get; set; }

    public string? LogisticRegionCode { get; set; }

    /// <summary>Cliente.</summary>
    public string? CardCode { get; set; }

    /// <summary>Início do período de ENTREGA. Filtra por sobreposição de janela.</summary>
    public DateTime? DeliveryFromDate { get; set; }

    public DateTime? DeliveryToDate { get; set; }
}
