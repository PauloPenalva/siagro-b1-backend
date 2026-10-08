using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros comuns aos relatórios de documentos fiscais (notas, itens e devoluções).
/// Só o período é obrigatório. Situação vazia = todas menos Cancelado; Situação NF-e
/// só vale em STANDALONE (em SAPB1 a NF-e não é emitida pelo Siagro e o filtro é ignorado).
/// </summary>
public abstract class InvoiceReportRequest
{
    /// <summary>Início do período de emissão.</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Fim do período de emissão, inclusivo até o fim do dia.</summary>
    public DateTime ToDate { get; set; }

    public string? BranchCode { get; set; }

    /// <summary>Cliente (saída) ou emitente (entrada).</summary>
    public string? CardCode { get; set; }

    public string? ItemCode { get; set; }

    public List<InvoiceStatus>? Statuses { get; set; }

    public List<NfeStatus>? NfeStatuses { get; set; }
}
