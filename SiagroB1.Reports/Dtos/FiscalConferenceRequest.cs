namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros da Conferência Fiscal: filial (opcional) e mês/ano do período.</summary>
public class FiscalConferenceRequest
{
    /// <summary>Filial; vazio = todas.</summary>
    public string? BranchCode { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }
}
