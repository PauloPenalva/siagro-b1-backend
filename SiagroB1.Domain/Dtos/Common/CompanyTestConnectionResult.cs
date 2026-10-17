namespace SiagroB1.Domain.Dtos.Common;

/// <summary>Resposta da action CompaniesTestConnection — o que a tela mostra no "Testar conexão".</summary>
public class CompanyTestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public List<string> PendingMigrations { get; set; } = [];
}
