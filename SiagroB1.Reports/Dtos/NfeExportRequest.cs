namespace SiagroB1.Reports.Dtos;

/// <summary>Pedido da exportação de NF-e para o contador (período pela data de autorização).</summary>
public sealed class NfeExportRequest
{
    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    /// <summary>Vazio = todas as filiais.</summary>
    public string? BranchCode { get; set; }

    public bool IncludeDanfe { get; set; } = true;

    /// <summary>Inclui os XMLs de terceiros recebidos pela Distribuição de DF-e.</summary>
    public bool IncludeReceived { get; set; } = true;
}
