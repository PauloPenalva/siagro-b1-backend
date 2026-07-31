using Hangfire;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Users;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Varredura periódica do cadastro de usuários do SAP (OUSR).
///
/// O provisionamento no login já cobre quem está entrando; esta varredura existe para o que só
/// se enxerga olhando o cadastro inteiro: usuários novos que ainda não logaram, nomes alterados
/// no SAP e, principalmente, usuários que sumiram de lá e precisam ser desativados aqui.
///
/// Registrado apenas quando <c>Erp = SAPB1</c>.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class SapUserSyncJob(
    SapUserSyncService service,
    ILogger<SapUserSyncJob> logger) : ISapUserSyncJob
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        try
        {
            await service.ExecuteAsync(ct);
        }
        catch (Exception exception)
        {
            // O SAP indisponível não pode encher o dashboard do Hangfire de falhas: a próxima
            // execução, minutos depois, resolve sozinha.
            logger.LogError(exception, "Falha na sincronização de usuários com o SAP.");
        }
    }
}
