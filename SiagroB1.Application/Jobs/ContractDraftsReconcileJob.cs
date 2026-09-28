using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Rede de segurança do webhook: se um evento do D4Sign se perder (URL fora do ar, sete tentativas
/// esgotadas), a minuta ficaria parada para sempre. A cada 30 min, olha as que não são conferidas
/// há mais de 6 h e busca a situação no provedor.
///
/// Sem retry do Hangfire: a próxima execução já é a retentativa, e uma falha do provedor não
/// melhora em segundos.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class ContractDraftsReconcileJob(
    AppDbContext context,
    IServiceScopeFactory scopeFactory,
    ILogger<ContractDraftsReconcileJob> logger)
{
    public const string RecurringJobId = "contract-drafts-reconcile";
    public const string CronExpression = "*/30 * * * *";

    private const int BatchSize = 50;
    private static readonly TimeSpan Staleness = TimeSpan.FromHours(6);

    /// <returns>Quantas minutas mudaram de estado.</returns>
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.Now - Staleness;

        // Duas populações: as que ainda correm, e as que ficaram assinadas SEM o PDF assinado —
        // sem a segunda, um download que falhou uma vez nunca mais é tentado.
        //
        // A antiguidade sai do LastCheckedAt, não do UpdatedAt: o UpdatedAt só anda quando alguma
        // coisa muda, então uma minuta parada em AwaitingSignature porque ninguém assinou seguiria
        // eternamente sendo a mais antiga do lote e, passando de 50 nessa situação, minuta nova
        // nenhuma seria alcançada. O fallback para UpdatedAt é para as linhas anteriores ao campo,
        // que têm LastCheckedAt nulo e de outro modo pareceriam todas recém-conferidas.
        var keys = await context.ContractDrafts.AsNoTracking()
            .Where(d => (d.Status == ContractDraftStatus.AwaitingSignature
                         || d.Status == ContractDraftStatus.PartiallySigned
                         || (d.Status == ContractDraftStatus.Signed && d.SignedAttachmentKey == null))
                        && d.ExternalDocumentId != null
                        && (d.LastCheckedAt ?? d.UpdatedAt) < cutoff)
            .OrderBy(d => d.LastCheckedAt ?? d.UpdatedAt)
            .Take(BatchSize)
            .Select(d => d.Key)
            .ToListAsync(ct);

        if (keys.Count == 0) return 0;

        var changed = 0;
        var examined = new List<Guid>(keys.Count);

        foreach (var key in keys)
        {
            // Um escopo — e portanto um AppDbContext — POR MINUTA. Com um contexto só para o lote
            // inteiro, quando o ApplyProviderState faz rollback e relança, o job segue adiante mas
            // o change tracker continua segurando a entidade modificada da minuta N e o anexo que
            // ela chegou a salvar, agora marcado Unchanged embora o banco o tenha desfeito. O
            // SaveChangesAsync da minuta N+1 despejaria tudo isso dentro da transação de N+1,
            // podendo commitar N como Signed apontando para um anexo inexistente — o que ainda por
            // cima desliga a recuperação, porque RetryMissingPdfAsync e a segunda população da
            // consulta acima só enxergam SignedAttachmentKey NULO.
            using var scope = scopeFactory.CreateScope();
            var refreshState = scope.ServiceProvider.GetRequiredService<ContractDraftsRefreshStateService>();

            examined.Add(key);

            try
            {
                if (await refreshState.ExecuteAsync(key, "reconciliacao", ct)) changed++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Uma minuta problemática não pode parar as outras 49. Desligamento do Hangfire,
                // porém, não é minuta problemática: sem este filtro, um shutdown no meio do lote
                // vira 49 erros de reconciliação no log em vez de uma saída limpa.
                logger.LogError(e, "Falha ao reconciliar a minuta {Key}", key);
            }
        }

        await RecordAttemptAsync(examined, ct);

        logger.LogInformation("Reconciliação de minutas: {Changed} de {Total} atualizadas", changed, keys.Count);
        return changed;
    }

    /// <summary>
    /// Carimba o <c>LastCheckedAt</c> de todas as minutas OLHADAS — tenham mudado, falhado ou
    /// continuado exatamente iguais. É justamente a que nunca muda que precisa sair da frente do
    /// lote. Roda no contexto do próprio job, com as linhas carregadas só agora (depois das
    /// escritas dos escopos) e uma única propriedade modificada, de modo que o UPDATE gerado toca
    /// apenas esta coluna e não tem como desfazer o que os escopos gravaram.
    /// </summary>
    private async Task RecordAttemptAsync(List<Guid> examined, CancellationToken ct)
    {
        if (examined.Count == 0) return;

        var now = DateTime.Now;
        var rows = await context.ContractDrafts.Where(d => examined.Contains(d.Key)).ToListAsync(ct);
        foreach (var row in rows) row.LastCheckedAt = now;

        await context.SaveChangesAsync(ct);
    }
}
