using System.Globalization;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public sealed record DfeSyncResult(int NewDocuments, int UpdatedDocuments, DateTimeOffset? NextQueryAllowedAt, string Message);

/// <summary>
/// Horários agendados pelo usuário (hora de Brasília, "HH:mm" separados por ";"). Vence o horário de hoje que já
/// passou e ainda não foi coberto pela última execução agendada: quem perdeu 12:00 e 20:00 roda UMA vez, não duas.
/// </summary>
public static class DfeSchedule
{
    public const int MaxTimes = 6;

    public static readonly TimeSpan MinGap = TimeSpan.FromHours(1);

    public static IReadOnlyList<TimeOnly> Parse(string? scheduleTimes)
    {
        if (string.IsNullOrWhiteSpace(scheduleTimes))
            return [];

        var times = new List<TimeOnly>();

        foreach (var part in scheduleTimes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (TimeOnly.TryParseExact(part, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                times.Add(time);

        return times.Distinct().Order().ToList();
    }

    public static bool IsDue(string? scheduleTimes, DateTimeOffset? lastScheduledRunAt, DateTimeOffset nowBrasilia)
    {
        foreach (var time in Parse(scheduleTimes))
        {
            var instant = new DateTimeOffset(nowBrasilia.Date + time.ToTimeSpan(), nowBrasilia.Offset);

            if (instant <= nowBrasilia && (lastScheduledRunAt is null || instant > lastScheduledRunAt))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Sincroniza os DF-e destinados à filial (Distribuição de DF-e por NSU) e guarda o cursor por filial. Só STANDALONE
/// (o certificado vem da Configuração da NF-e). A SEFAZ limita a consulta: 137/656 exigem esperar 1 hora.
/// </summary>
public class ReceivedDfeSyncService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    ILogger<ReceivedDfeSyncService> logger,
    Func<DateTimeOffset>? clock = null)
{
    public const int MaxCallsPerRun = 20;

    private static readonly TimeSpan Wait = TimeSpan.FromHours(1);

    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    private static readonly int[] ManifestationEvents = [210200, 210210, 210220, 210240];

    public async Task<DfeSyncResult> SyncBranchAsync(string branchCode, CancellationToken ct = default)
    {
        var now = _now();
        var context = db.Context;

        var branch = await context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode, ct)
                     ?? throw new DefaultException($"Filial {branchCode} não encontrada.");

        var state = await LoadOrCreateStateAsync(branchCode, ct);

        if (state.NextQueryAllowedAt is { } allowed && allowed > now)
            return new DfeSyncResult(0, 0, allowed, WaitMessage(allowed));

        var taxId = NfeText.AlphaNumeric(branch.TaxId);
        if (taxId.Length == 0)
            throw new DefaultException($"A filial {branchCode} está sem CNPJ.");

        using var service = await settingsService.OpenAsync(branchCode);

        var created = 0;
        var updated = 0;
        var finished = false;
        string? failure = null;

        for (var call = 0; call < MaxCallsPerRun && !finished; call++)
        {
            var result = await sefaz.DistributionByNsuAsync(taxId, state.LastNsu, service.Settings, ct);
            state.LastQueryAt = now;

            if (result.StatusCode == NfeStatusCodes.ExcessiveUse)
            {
                // Consumo indevido: o cursor devolvido não é confiável, só a espera vale.
                state.NextQueryAllowedAt = now + Wait;
                finished = true;
            }
            else if (result.StatusCode is NfeStatusCodes.NoDocuments or NfeStatusCodes.DocumentsFound)
            {
                foreach (var document in result.Documents)
                {
                    var (isNew, isUpdated) = await ApplyAsync(branchCode, document, now, ct);
                    if (isNew) created++;
                    if (isUpdated) updated++;
                }

                // Nunca recua o cursor; grava a cada lote para que uma queda no meio não repita o trabalho.
                if (result.LastNsu > state.LastNsu)
                    state.LastNsu = result.LastNsu;

                if (result.MaxNsu > 0)
                    state.MaxNsu = result.MaxNsu;

                if (result.StatusCode == NfeStatusCodes.NoDocuments || state.LastNsu >= state.MaxNsu)
                {
                    state.NextQueryAllowedAt = now + Wait;
                    finished = true;
                }
            }
            else
            {
                failure = $"A SEFAZ recusou a consulta: {result.StatusCode} - {result.Reason}";
                finished = true;
            }

            await db.SaveChangesAsync();
        }

        if (failure is not null)
            throw new DefaultException(failure);

        var message = state.NextQueryAllowedAt is { } next
            ? $"Consulta concluída: {created} novo(s), {updated} atualizado(s). Próxima consulta a partir de {Format(next)}."
            : $"Consulta parcial: {created} novo(s), {updated} atualizado(s). Há mais documentos; consulte de novo.";

        return new DfeSyncResult(created, updated, state.NextQueryAllowedAt, message);
    }

    /// <summary>Filiais com certificado e horário vencido. Um horário dentro da espera da SEFAZ é pulado e conta como rodado.</summary>
    public async Task RunScheduledAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var nowBrasilia = TimeZoneInfo.ConvertTime(now, NfeIssueInputAssembler.BrasiliaZone);

        var candidates = await (
            from s in db.Context.BranchDfeSyncStates.AsNoTracking()
            join n in db.Context.BranchNfeSettings.AsNoTracking() on s.BranchCode equals n.BranchCode
            where s.ScheduleTimes != null && s.ScheduleTimes != "" && n.CertificatePfx != null
            select s.BranchCode).ToListAsync(ct);

        foreach (var branchCode in candidates)
        {
            try
            {
                var state = await db.Context.BranchDfeSyncStates.FirstAsync(s => s.BranchCode == branchCode, ct);

                if (!DfeSchedule.IsDue(state.ScheduleTimes, state.LastScheduledRunAt, nowBrasilia))
                    continue;

                // Marca antes de consultar: uma falha não pode fazer o mesmo horário repetir a cada 5 minutos.
                state.LastScheduledRunAt = now;
                await db.SaveChangesAsync();

                if (state.NextQueryAllowedAt is { } allowed && allowed > now)
                {
                    logger.LogInformation(
                        "Consulta de DF-e da filial {Branch} pulada: a SEFAZ só permite a partir de {Allowed}.", branchCode, allowed);
                    continue;
                }

                await SyncBranchAsync(branchCode, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Uma filial com problema (certificado vencido, SEFAZ fora) não pode parar as outras.
                logger.LogError(e, "Falha na consulta agendada de DF-e da filial {Branch}.", branchCode);
                db.Context.ChangeTracker.Clear();
            }
        }
    }

    public async Task<BranchDfeSyncState> SetScheduleAsync(string branchCode, IReadOnlyList<string> times, CancellationToken ct = default)
    {
        if (!await db.Context.Branchs.AnyAsync(b => b.Code == branchCode, ct))
            throw new DefaultException($"Filial {branchCode} não encontrada.");

        var parsed = new List<TimeOnly>();

        foreach (var text in times)
        {
            if (!TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new DefaultException($"Horário inválido: \"{text}\". Use o formato HH:mm.");

            parsed.Add(time);
        }

        parsed = parsed.Distinct().Order().ToList();

        if (parsed.Count > DfeSchedule.MaxTimes)
            throw new DefaultException($"Informe no máximo {DfeSchedule.MaxTimes} horários.");

        for (var i = 1; i < parsed.Count; i++)
            if (parsed[i].ToTimeSpan() - parsed[i - 1].ToTimeSpan() < DfeSchedule.MinGap)
                throw new DefaultException("Os horários precisam ter pelo menos 1 hora de intervalo.");

        var state = await LoadOrCreateStateAsync(branchCode, ct);
        state.ScheduleTimes = parsed.Count == 0 ? null : string.Join(';', parsed.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)));

        await db.SaveChangesAsync();

        return state;
    }

    private async Task<BranchDfeSyncState> LoadOrCreateStateAsync(string branchCode, CancellationToken ct)
    {
        var state = await db.Context.BranchDfeSyncStates.FirstOrDefaultAsync(s => s.BranchCode == branchCode, ct);

        if (state is not null)
            return state;

        state = new BranchDfeSyncState { BranchCode = branchCode };
        db.Context.BranchDfeSyncStates.Add(state);

        return state;
    }

    private async Task<(bool IsNew, bool IsUpdated)> ApplyAsync(
        string branchCode, NfeDistributionDocument document, DateTimeOffset now, CancellationToken ct)
    {
        DfeParsed parsed;

        try
        {
            parsed = NfeDistributionDocumentParser.Parse(document);
        }
        catch (XmlException e)
        {
            // Um documento ilegível não pode travar o cursor do NSU.
            logger.LogWarning(e, "DF-e NSU {Nsu} da filial {Branch} com XML inválido: ignorado.", document.Nsu, branchCode);
            return (false, false);
        }

        if (parsed.AccessKey is not { Length: 44 } key)
            return (false, false);

        var context = db.Context;
        var row = context.ReceivedDfes.Local.FirstOrDefault(x => x.BranchCode == branchCode && x.AccessKey == key)
                  ?? await context.ReceivedDfes.FirstOrDefaultAsync(x => x.BranchCode == branchCode && x.AccessKey == key, ct);

        switch (parsed.Kind)
        {
            case DfeKind.Summary or DfeKind.FullInvoice:
            {
                var isNew = row is null;

                row ??= context.ReceivedDfes.Add(new ReceivedDfe
                {
                    BranchCode = branchCode, AccessKey = key, Nsu = document.Nsu, CreatedAt = now,
                }).Entity;

                row.IssuerTaxId = parsed.IssuerTaxId ?? row.IssuerTaxId;
                row.IssuerName = Truncate(parsed.IssuerName, 200) ?? row.IssuerName;
                row.IssueDate = parsed.IssueDate ?? row.IssueDate;
                row.TotalValue = parsed.TotalValue ?? row.TotalValue;

                // Cancelada não volta a autorizada por um resumo repetido.
                if (parsed.InvoiceSituation is { } code && Enum.IsDefined((DfeInvoiceSituation)code) && row.Situation != DfeInvoiceSituation.Cancelled)
                    row.Situation = (DfeInvoiceSituation)code;

                if (parsed.Kind == DfeKind.FullInvoice)
                {
                    row.Xml = parsed.FullXml;
                    row.XmlDownloadedAt = now;
                }

                return (isNew, !isNew);
            }

            case DfeKind.Event when row is not null:
            {
                if (parsed.EventType == 110111)
                {
                    row.Situation = DfeInvoiceSituation.Cancelled;
                    return (false, true);
                }

                if (parsed.EventType is { } type && ManifestationEvents.Contains(type)
                    && ManifestationOf(type) is var status && Rank(status) > Rank(row.ManifestationStatus))
                {
                    row.ManifestationStatus = status;
                    return (false, true);
                }

                return (false, false);
            }

            default:
                return (false, false);
        }
    }

    private static DfeManifestationStatus ManifestationOf(int type) => type switch
    {
        210200 => DfeManifestationStatus.Confirmed,
        210210 => DfeManifestationStatus.Acknowledged,
        210220 => DfeManifestationStatus.Unknown,
        _ => DfeManifestationStatus.NotPerformed,
    };

    /// <summary>A ciência é a etapa inicial; qualquer manifestação conclusiva vem depois dela.</summary>
    private static int Rank(DfeManifestationStatus status) => status switch
    {
        DfeManifestationStatus.None => 0,
        DfeManifestationStatus.Acknowledged => 1,
        _ => 2,
    };

    private static string? Truncate(string? value, int length) =>
        value is null ? null : value.Length <= length ? value : value[..length];

    private static string Format(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, NfeIssueInputAssembler.BrasiliaZone).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string WaitMessage(DateTimeOffset allowed) => $"Aguarde até {Format(allowed)} para consultar novamente.";
}
