using Hangfire;
using SiagroB1.Application.Services.SAP;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Sincroniza, por empresa, o cancelamento no SAP das invoices vinculadas a documentos de saída (GAC-1226).
/// O Hangfire dispara a cada minuto; o serviço confere o intervalo configurado da empresa e só então executa.
/// Sem retry do Hangfire: a próxima execução já é a retentativa. Registrado apenas em <c>Erp = SAPB1</c>.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class SapInvoiceSyncJob(SapInvoiceSyncService service)
{
    public const string CronExpression = "* * * * *";

    public Task ExecuteAsync(CancellationToken ct = default) => service.ExecuteAsync(ct);
}
