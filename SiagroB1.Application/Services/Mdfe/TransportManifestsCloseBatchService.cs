using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>Resultado do encerramento de um MDF-e dentro do lote.</summary>
public record MdfeCloseBatchItem(Guid Key, int? Number, bool Success, string Message);

/// <summary>
/// Encerra vários MDF-e, um a um e de forma independente (síncrono, sem Hangfire): cada item toma o bloqueio de emissão,
/// reconfere a situação dentro dele e chama o núcleo do encerramento com os padrões do próprio MDF-e (último município
/// de descarga e UF fim). Falha de um item (não encontrado, não autorizado, bloqueio, sem resposta da SEFAZ, rejeição)
/// vira mensagem do item e não desfaz nem interrompe os demais. O certificado é aberto uma vez por filial/ambiente.
/// </summary>
public class TransportManifestsCloseBatchService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeNumberReservationService numbers,
    MdfeServiceContextFactory contexts,
    TransportManifestsCloseService closeService,
    ILogger<TransportManifestsCloseBatchService>? logger = null)
{
    public const string Closed = "MDF-e encerrado.";
    public const string UnexpectedError = "Erro inesperado ao encerrar; consulte o MDF-e antes de tentar de novo.";
    public const int MaxKeys = 50;

    public async Task<IReadOnlyList<MdfeCloseBatchItem>> CloseBatchAsync(IReadOnlyList<Guid> keys, DateOnly? closedOn, string user)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        MdfeZeusConfiguration.PinGlobalLayout();

        var results = new List<MdfeCloseBatchItem>();
        var opened = new Dictionary<(string Branch, NfeEnvironment Environment), MdfeServiceContext>();
        try
        {
            foreach (var key in keys.Distinct())
                results.Add(await CloseOneAsync(key, closedOn, user, opened));
        }
        finally
        {
            foreach (var context in opened.Values) context.Dispose();
        }

        return results;
    }

    private async Task<MdfeCloseBatchItem> CloseOneAsync(
        Guid key, DateOnly? closedOn, string user, Dictionary<(string, NfeEnvironment), MdfeServiceContext> opened)
    {
        int? number = null;
        try
        {
            await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

            // Atenção: o Clear descarta tudo que o UnitOfWork do request já rastreava; a action não pode ler entidades rastreadas depois do lote.
            // Entidade rastreada de lote anterior não pode mascarar o estado atual do banco.
            db.Context.ChangeTracker.Clear();
            var m = await MdfeEmission.LoadAsync(db, key);
            number = m.Number;
            if (m.Status != MdfeStatus.Authorized)
                return new MdfeCloseBatchItem(key, number, false, TransportManifestsCloseService.NotAuthorized);

            var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstAsync(x => x.BranchCode == m.BranchCode);
            var environment = m.Environment ?? settings.Environment;
            if (!opened.TryGetValue((m.BranchCode, environment), out var context))
                opened[(m.BranchCode, environment)] = context = await contexts.OpenAsync(m.BranchCode, environment);

            var result = await closeService.CloseCoreAsync(m, context.Settings, closedOn, null, null, user);
            return NfeStatusCodes.IsEventRegistered(result.StatusCode)
                ? new MdfeCloseBatchItem(key, number, true, Closed)
                : new MdfeCloseBatchItem(key, number, false, $"SEFAZ {result.StatusCode}: {result.Reason}");
        }
        catch (DefaultException e)
        {
            return new MdfeCloseBatchItem(key, number, false, e.Message);
        }
        catch (NfeCommunicationException)
        {
            return new MdfeCloseBatchItem(key, number, false, TransportManifestsCloseService.NoResponse);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Ex.: falha ao gravar depois de a SEFAZ registrar o encerramento, certificado inválido, configuração ausente.
            logger?.LogError(e, "Erro inesperado ao encerrar o MDF-e {Key} em lote.", key);
            return new MdfeCloseBatchItem(key, number, false, UnexpectedError);
        }
    }
}
