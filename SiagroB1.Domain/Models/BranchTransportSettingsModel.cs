using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>
/// Configuração de transporte da filial na API. A senha da AT&amp;M só entra (<see cref="AverbaPassword"/>,
/// vazia mantém a gravada); na saída vai apenas <see cref="HasAverbaPassword"/>.
/// </summary>
public class BranchTransportSettingsModel
{
    public string BranchCode { get; set; } = string.Empty;
    public string? CompanyRntrc { get; set; }
    public InsuranceResponsible? InsuranceResponsible { get; set; }
    public string? InsurerTaxId { get; set; }
    public string? InsurerName { get; set; }
    public string? PolicyNumber { get; set; }
    public bool AverbaEnabled { get; set; }
    public string? AverbaUser { get; set; }
    public string? AverbaPassword { get; set; }
    public bool HasAverbaPassword { get; set; }
    public string? AverbaCode { get; set; }
    public NfeEnvironment AverbaEnvironment { get; set; } = NfeEnvironment.Production;
}

/// <summary>Resultado do "Testar conexão" com a AT&amp;M.</summary>
public class AverbaTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
