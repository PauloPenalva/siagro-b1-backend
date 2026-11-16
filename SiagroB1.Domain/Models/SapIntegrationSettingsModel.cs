namespace SiagroB1.Domain.Models;

/// <summary>
/// Configuração da integração SAP como a tela vê. <see cref="InvoiceSyncLastRunAt"/> é só leitura:
/// o servidor ignora o valor que vier do cliente.
/// </summary>
public class SapIntegrationSettingsModel
{
    public bool InvoiceSyncEnabled { get; set; } = true;
    public int InvoiceSyncIntervalMinutes { get; set; } = 15;
    public DateTime? InvoiceSyncLastRunAt { get; set; }
}
