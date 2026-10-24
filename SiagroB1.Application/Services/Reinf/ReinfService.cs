using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Fiscal.Reinf.Events;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Reinf;

/// <summary>
/// Transmissão da EFD-Reinf (R-2055): compara a apuração do período com os eventos já enviados,
/// gera R-2055 (original/retificação) ou R-9000 (exclusão), assina, valida no XSD, envia em lotes
/// de até 50 e consulta o retorno do lote. Só STANDALONE.
/// </summary>
public class ReinfService(
    AppDbContext context,
    IConfiguration configuration,
    ReinfAcquisitionCalculator calculator,
    IReinfClient client,
    IReinfCertificateProvider certificates,
    ReinfEventIdGenerator ids,
    ReinfSchemaValidator validator,
    ReinfAppInfo app,
    TimeProvider time) : IReinfService
{
    /// <summary>Mesmo texto de <c>FiscalConferenceController.StandaloneOnlyMessage</c> (que fica no Reports).</summary>
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";
    public const string RegistrationRequiredMessage = "Envie e tenha aceito o R-1000 antes.";
    public const string PeriodClosedMessage = "Período fechado: reabra (R-2098) antes de enviar.";

    private static readonly ReinfEventType[] ProducerTypes = [ReinfEventType.R2055, ReinfEventType.R9000];

    public async Task<ReinfPeriodOverview> GetOverviewAsync(string taxIdRoot, string period, CancellationToken ct)
    {
        EnsureStandalone();
        var root = ReinfFormat.Root(taxIdRoot);
        var (rows, issues) = await calculator.CalculateAsync(root, period, ct);
        var state = await LoadStateAsync(root, period, ct);
        var periodEntity = await context.ReinfPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.TaxIdRoot == root && p.Period == period, ct);

        var result = new List<ReinfOverviewRow>();
        foreach (var row in rows)
            result.Add(Situation(row, state.For(row.EstablishmentTaxId, row.CardCode)));
        foreach (var orphan in state.OrphanKeys(rows))
            result.Add(Situation(null, state.For(orphan.Establishment, orphan.CardCode)));

        return new ReinfPeriodOverview(root, period, periodEntity?.Status ?? ReinfPeriodStatus.Open, result, issues);
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    /// <summary>
    /// Serializa a transmissão por contribuinte + período: trava do processo e, no SQL Server,
    /// sp_getapplock com dono sessão, sem transação (vale entre servidores). Quem espera enxerga os eventos em voo.
    /// </summary>
    public Task<IReadOnlyList<Guid>> TransmitAsync(string taxIdRoot, string period, string user, CancellationToken ct)
    {
        EnsureStandalone();
        return LockedAsync(ReinfFormat.Root(taxIdRoot), period, () => TransmitCoreAsync(taxIdRoot, period, user, ct), ct);
    }

    private async Task<T> LockedAsync<T>(string root, string periodKey, Func<Task<T>> body, CancellationToken ct)
    {
        var key = $"reinf:{root}:{periodKey}";
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!context.Database.IsRelational())
                return await body();

            // Sem transação: cada SaveChanges confirma sozinho (o lote gravado antes do envio é durável).
            await context.Database.OpenConnectionAsync(ct);
            try
            {
                var code = await AppLockAsync("sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000", key, ct);
                if (code < 0)
                    throw new BusinessException("Outra transmissão da EFD-Reinf deste período está em andamento; tente novamente.");
                try
                {
                    return await body();
                }
                finally
                {
                    await AppLockAsync("sp_releaseapplock @Resource = @res, @LockOwner = 'Session'", key, CancellationToken.None);
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<int> AppLockAsync(string exec, string resource, CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DECLARE @r int; EXEC @r = {exec}; SELECT @r;";
        command.CommandTimeout = 75;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@res";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? -999 : Convert.ToInt32(value);
    }

    private async Task<IReadOnlyList<Guid>> TransmitCoreAsync(string taxIdRoot, string period, string user, CancellationToken ct)
    {
        var root = ReinfFormat.Root(taxIdRoot);
        var settings = await context.ReinfSettings.FirstOrDefaultAsync(s => s.TaxIdRoot == root, ct);
        if (settings is null || settings.RegistrationStatus != ReinfRegistrationStatus.Accepted)
            throw new BusinessException(RegistrationRequiredMessage);

        var periodEntity = await context.ReinfPeriods.FirstOrDefaultAsync(p => p.TaxIdRoot == root && p.Period == period, ct);
        if (periodEntity?.Status is ReinfPeriodStatus.Closed or ReinfPeriodStatus.Closing)
            throw new BusinessException(PeriodClosedMessage);

        var (rows, issues) = await calculator.CalculateAsync(root, period, ct);
        if (issues.Count > 0)
            throw new BusinessException(issues[0].Message + (issues.Count > 1 ? $" (+{issues.Count - 1} pendência(s))" : ""));

        var state = await LoadStateAsync(root, period, ct, tracked: true);
        var header = new ReinfHeader(settings.Environment, root, app.Version);

        var plan = new List<(ReinfEvent Event, Func<string, XmlDocument> Build)>();
        void Plan(ReinfEvent? reuse, Action<ReinfEvent> fill, Func<string, XmlDocument> build)
        {
            var e = reuse ?? new ReinfEvent { TaxIdRoot = root, Period = period, CreatedBy = user, CreatedAt = time.GetUtcNow().UtcDateTime };
            fill(e);
            plan.Add((e, build));
        }

        foreach (var row in rows)
        {
            var s = state.For(row.EstablishmentTaxId, row.CardCode);
            if (s.InFlight || s.Unconfirmed is not null) continue;
            var accepted = s.Accepted;
            if (row.Gross > 0)
            {
                if (accepted is not null && accepted.ContentHash == row.ContentHash) continue;
                var retif = accepted is null ? 1 : 2;
                var receipt = accepted?.Receipt;
                Plan(s.Retryable, e => Fill2055(e, row, retif, receipt),
                    id => R2055Builder.Build(new R2055Input(header, period, retif, receipt, row.EstablishmentTaxId,
                        row.ProducerInscriptionType, row.ProducerTaxId, row.PayrollOption, row.Details), id));
            }
            else if (accepted is not null)
            {
                PlanExclusion(accepted, s.Retryable);
            }
        }
        foreach (var orphan in state.OrphanKeys(rows))
        {
            var s = state.For(orphan.Establishment, orphan.CardCode);
            if (!s.InFlight && s.Unconfirmed is null && s.Accepted is not null) PlanExclusion(s.Accepted, s.Retryable);
        }

        void PlanExclusion(ReinfEvent target, ReinfEvent? reuse) =>
            Plan(reuse, e =>
            {
                e.Type = ReinfEventType.R9000;
                e.EstablishmentTaxId = target.EstablishmentTaxId;
                e.CardCode = target.CardCode;
                e.ProducerTaxId = target.ProducerTaxId;
                e.RectificationIndicator = 1;
                e.PreviousReceipt = target.Receipt;
                e.GrossValue = e.InssValue = e.RatValue = e.SenarValue = 0m;
                e.ContentHash = null;
            }, id => R9000Builder.Build(new R9000Input(header, "R-2055", target.Receipt!, period), id));

        if (plan.Count == 0) return [];

        return await SignAndSendAsync(root, settings, plan, ct);
    }

    private async Task<IReadOnlyList<Guid>> SignAndSendAsync(
        string root, ReinfSettings settings, List<(ReinfEvent Event, Func<string, XmlDocument> Build)> plan, CancellationToken ct)
    {
        var now = BrasiliaNow();
        using var cert = await certificates.GetAsync(root, ct);
        var signed = new List<(ReinfEvent Event, XmlDocument Doc)>();
        var errors = new List<string>();
        foreach (var (e, build) in plan)
        {
            e.EventId = ids.Next(root, now);
            var doc = ReinfSigner.Sign(build(e.EventId), cert);
            var found = validator.Validate(doc);
            if (found.Count > 0) errors.Add($"{e.Type} {e.CardCode}: {string.Join(" ", found)}");
            e.Xml = doc.OuterXml;
            e.Status = ReinfEventStatus.Pending;
            e.Receipt = null;
            e.Occurrences = null;
            e.BatchKey = null;
            e.UpdatedAt = time.GetUtcNow().UtcDateTime;
            signed.Add((e, doc));
        }
        if (errors.Count > 0)
            throw new BusinessException("Evento inválido no leiaute da EFD-Reinf: " + string.Join(" | ", errors));

        foreach (var (e, _) in signed)
            if (context.Entry(e).State == EntityState.Detached) context.ReinfEvents.Add(e);

        var batches = new List<Guid>();
        foreach (var chunk in signed.Chunk(ReinfBatchBuilder.MaxEvents))
        {
            var batch = new ReinfBatch
            {
                TaxIdRoot = root, Environment = settings.Environment, SentAt = time.GetUtcNow().UtcDateTime,
                RequestXml = ReinfBatchBuilder.Build(root, chunk.Select(c => (c.Event.EventId, c.Doc)).ToList()),
            };
            context.ReinfBatches.Add(batch);
            foreach (var (e, _) in chunk) e.BatchKey = batch.Key;
            batches.Add(batch.Key);
            // Grava lote e vínculo antes do envio: qualquer falha adiante deixa o registro.
            await context.SaveChangesAsync(ct);

            try
            {
                var result = await client.SendAsync(batch.RequestXml, cert, settings.Environment, ct);
                batch.Protocol = result.Protocol;
                batch.ResponseXml = result.RawXml;
                var events = chunk.Select(c => c.Event).ToList();
                if (result.ResponseCode is 1 or 2 or 3)
                {
                    batch.Status = ReinfBatchStatus.Sent;
                    foreach (var e in events) e.Status = ReinfEventStatus.Sent;
                    await context.SaveChangesAsync(ct);
                    if (result.ResponseCode is 2 or 3) await ApplyProcessedAsync(batch, events, result, ct);
                }
                else await RefuseAsync(batch, events, result, ct);
            }
            catch (ReinfUnknownOutcomeException ex)
            {
                // A Receita pode ter recebido o lote: não reenviar sozinho nem reverter controle (R-1000/R-2099/R-2098).
                batch.Status = ReinfBatchStatus.Failed;
                batch.Error = UnknownOutcomeMessage + " (" + ex.Message + ")";
                foreach (var (e, _) in chunk)
                {
                    e.Status = ReinfEventStatus.Unconfirmed;
                    e.Occurrences = UnknownOutcomeMessage;
                    e.UpdatedAt = time.GetUtcNow().UtcDateTime;
                }
            }
            catch (ReinfTransportException ex)
            {
                batch.Status = ReinfBatchStatus.Failed;
                batch.Error = $"HTTP {ex.StatusCode}: {ex.Body}";
                await RevertControlEventsAsync(chunk.Select(c => c.Event), batch.Error, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Sem protocolo, nada chegou de fato: Failed (eventos Pending, retentável). Com protocolo, segue Sent
                // e a consulta do lote resolve.
                batch.Error = ex.Message;
                if (string.IsNullOrWhiteSpace(batch.Protocol))
                {
                    batch.Status = ReinfBatchStatus.Failed;
                    await RevertControlEventsAsync(chunk.Select(c => c.Event), ex.Message, CancellationToken.None);
                }
                await context.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            await context.SaveChangesAsync(ct);
        }
        return batches;
    }

    public async Task QueryBatchAsync(Guid batchKey, CancellationToken ct)
    {
        EnsureStandalone();
        var batch = await context.ReinfBatches.FirstOrDefaultAsync(b => b.Key == batchKey, ct)
                    ?? throw new BusinessException("Lote da EFD-Reinf não encontrado.");
        if (string.IsNullOrWhiteSpace(batch.Protocol))
            throw new BusinessException("Lote sem protocolo: transmita novamente.");

        using var cert = await certificates.GetAsync(batch.TaxIdRoot, ct);
        var result = await client.QueryAsync(batch.Protocol, cert, batch.Environment, ct);
        batch.Attempts++;
        batch.ResponseXml = result.RawXml;

        var events = await context.ReinfEvents.Where(e => e.BatchKey == batchKey).ToListAsync(ct);
        if (result.ResponseCode == 1)
            batch.Status = ReinfBatchStatus.Processing;
        else if (result.ResponseCode is 2 or 3)
            await ApplyProcessedAsync(batch, events, result, ct);
        else
            await RefuseAsync(batch, events, result, ct);

        await context.SaveChangesAsync(ct);
    }

    public const string UnknownOutcomeMessage = "Sem confirmação da Receita: o lote pode ter sido recebido. Confira no portal antes de reenviar.";
    public const string StaleBatchMessage = "Lote sem retorno após 30 consultas automáticas; use Consultar lotes.";
    public const int MaxAutomaticQueries = 30;

    /// <summary>
    /// Libera os eventos Unconfirmed do período (e do R-1000): R-2055/R-9000 voltam a Pendente e são regerados no
    /// próximo envio; R-2099/R-2098/R-1000 viram Rejeitados e devolvem período/cadastro à situação anterior.
    /// </summary>
    public Task<int> ReleaseUnconfirmedAsync(string taxIdRoot, string? period, CancellationToken ct)
    {
        EnsureStandalone();
        var root = ReinfFormat.Root(taxIdRoot);
        return LockedAsync(root, period ?? RegistrationPeriodKey, async () =>
        {
            var events = await context.ReinfEvents.Where(e => e.TaxIdRoot == root && e.Status == ReinfEventStatus.Unconfirmed
                && ((period != null && e.Period == period) || (e.Period == null && e.Type == ReinfEventType.R1000))).ToListAsync(ct);
            foreach (var e in events)
            {
                e.UpdatedAt = time.GetUtcNow().UtcDateTime;
                if (ProducerTypes.Contains(e.Type))
                {
                    e.Status = ReinfEventStatus.Pending;
                    continue;
                }
                e.Status = ReinfEventStatus.Rejected;
                await OnRejectedAsync(e, ct);
            }
            await context.SaveChangesAsync(ct);
            return events.Count;
        }, ct);
    }

    public const string RegistrationPeriodKey = "R1000";
    public const string SoftwareHouseMissingMessage = "Configure Reinf:SoftwareHouse no appsettings.";
    public const string PendingEventsMessage = "Há eventos pendentes no período; transmita e aguarde o aceite antes de fechar.";
    public const string ReopenOnlyClosedMessage = "Só é possível reabrir um período fechado.";

    private static readonly string[] BlockingSituations = ["Novo", "Desatualizado", "Excluir", "Rejeitado", "Enviado", "Verificar"];

    /// <summary>Inclusão enquanto o R-1000 nunca foi aceito; alteração depois (já há recibo).</summary>
    public static R1000Operation RegistrationOperation(ReinfSettings s) =>
        s.RegistrationReceipt is null ? R1000Operation.Include : R1000Operation.Update;

    public Task<Guid> SendRegistrationAsync(string taxIdRoot, string user, CancellationToken ct)
    {
        EnsureStandalone();
        var root = ReinfFormat.Root(taxIdRoot);
        return LockedAsync(root, RegistrationPeriodKey, async () =>
        {
            var softHouse = configuration.GetSection("Reinf:SoftwareHouse").Get<ReinfSoftwareHouseOptions>();
            if (softHouse is null || string.IsNullOrWhiteSpace(softHouse.Cnpj) || string.IsNullOrWhiteSpace(softHouse.Name))
                throw new BusinessException(SoftwareHouseMissingMessage);

            var settings = await context.ReinfSettings.FirstOrDefaultAsync(s => s.TaxIdRoot == root, ct)
                           ?? throw new BusinessException("Cadastre os parâmetros da EFD-Reinf antes.");
            if (settings.RegistrationStatus == ReinfRegistrationStatus.Sent)
                throw new BusinessException("R-1000 já enviado; aguarde o retorno da Receita.");

            var header = new ReinfHeader(settings.Environment, root, app.Version);
            var input = new R1000Input(header, RegistrationOperation(settings), settings.ValidFrom, settings.TaxClassification,
                settings.KeepsBookkeeping, settings.PayrollRelief, settings.FineExemptionAgreement, settings.CompanySituation,
                settings.ContactName, settings.ContactCpf, settings.ContactPhone, settings.ContactEmail,
                new SoftwareHouse(softHouse.Cnpj, softHouse.Name,
                    string.IsNullOrWhiteSpace(softHouse.Contact) ? softHouse.Name : softHouse.Contact, softHouse.Phone, softHouse.Email));
            var e = NewControlEvent(ReinfEventType.R1000, root, null, user);
            settings.RegistrationStatus = ReinfRegistrationStatus.Sent;
            var batches = await SignAndSendAsync(root, settings, [(e, id => R1000Builder.Build(input, id))], ct);
            return batches[0];
        }, ct);
    }

    public async Task<Guid> CloseAsync(string taxIdRoot, string period, string user, CancellationToken ct)
    {
        EnsureStandalone();
        var root = ReinfFormat.Root(taxIdRoot);
        var overview = await GetOverviewAsync(root, period, ct);
        if (overview.Rows.Any(r => BlockingSituations.Contains(r.Situation)))
            throw new BusinessException(PendingEventsMessage);

        return await LockedAsync(root, period, async () =>
        {
            var settings = await RequireRegistrationAsync(root, ct);
            if (await context.ReinfEvents.AnyAsync(e => e.TaxIdRoot == root && e.Period == period
                    && (e.Status == ReinfEventStatus.Pending || e.Status == ReinfEventStatus.Sent || e.Status == ReinfEventStatus.Unconfirmed), ct))
                throw new BusinessException(PendingEventsMessage);

            var p = await context.ReinfPeriods.FirstOrDefaultAsync(x => x.TaxIdRoot == root && x.Period == period, ct);
            if (p is null) { p = new ReinfPeriod { TaxIdRoot = root, Period = period }; context.ReinfPeriods.Add(p); }
            if (p.Status != ReinfPeriodStatus.Open)
                throw new BusinessException("Período já fechado ou com fechamento/reabertura em andamento.");

            var hasAcquisitions = await context.ReinfEvents.AnyAsync(e => e.TaxIdRoot == root && e.Period == period
                && e.Type == ReinfEventType.R2055 && e.Status == ReinfEventStatus.Accepted, ct);
            var useResponsible = !string.IsNullOrWhiteSpace(settings.ClosingResponsibleName)
                                 && !string.IsNullOrWhiteSpace(settings.ClosingResponsibleCpf);
            var input = new R2099Input(new ReinfHeader(settings.Environment, root, app.Version), period,
                useResponsible ? settings.ClosingResponsibleName! : settings.ContactName,
                useResponsible ? settings.ClosingResponsibleCpf! : settings.ContactCpf,
                useResponsible ? settings.ClosingResponsiblePhone : settings.ContactPhone,
                useResponsible ? settings.ClosingResponsibleEmail : settings.ContactEmail,
                hasAcquisitions);

            p.Status = ReinfPeriodStatus.Closing;
            var e = NewControlEvent(ReinfEventType.R2099, root, period, user);
            var batches = await SignAndSendAsync(root, settings, [(e, id => R2099Builder.Build(input, id))], ct);
            return batches[0];
        }, ct);
    }

    public Task<Guid> ReopenAsync(string taxIdRoot, string period, string user, CancellationToken ct)
    {
        EnsureStandalone();
        var root = ReinfFormat.Root(taxIdRoot);
        return LockedAsync(root, period, async () =>
        {
            var p = await context.ReinfPeriods.FirstOrDefaultAsync(x => x.TaxIdRoot == root && x.Period == period, ct);
            if (p is not { Status: ReinfPeriodStatus.Closed })
                throw new BusinessException(ReopenOnlyClosedMessage);
            var settings = await RequireRegistrationAsync(root, ct);
            var input = new R2098Input(new ReinfHeader(settings.Environment, root, app.Version), period);
            p.Status = ReinfPeriodStatus.Reopening;
            var e = NewControlEvent(ReinfEventType.R2098, root, period, user);
            var batches = await SignAndSendAsync(root, settings, [(e, id => R2098Builder.Build(input, id))], ct);
            return batches[0];
        }, ct);
    }

    private async Task<ReinfSettings> RequireRegistrationAsync(string root, CancellationToken ct)
    {
        var settings = await context.ReinfSettings.FirstOrDefaultAsync(s => s.TaxIdRoot == root, ct);
        if (settings is null || settings.RegistrationStatus != ReinfRegistrationStatus.Accepted)
            throw new BusinessException(RegistrationRequiredMessage);
        return settings;
    }

    private ReinfEvent NewControlEvent(ReinfEventType type, string root, string? period, string user) => new()
    {
        Type = type, TaxIdRoot = root, Period = period, CreatedBy = user, CreatedAt = time.GetUtcNow().UtcDateTime,
    };

    private async Task RefuseAsync(ReinfBatch batch, IReadOnlyList<ReinfEvent> events, ReinfBatchResult result, CancellationToken ct)
    {
        batch.Status = ReinfBatchStatus.Refused;
        batch.ProcessedAt = time.GetUtcNow().UtcDateTime;
        var text = string.Join(" | ", new[] { $"{result.ResponseCode} - {result.Description}" }.Concat(result.Occurrences));
        batch.Error = text;
        foreach (var e in events)
        {
            e.Status = ReinfEventStatus.Pending;
            e.Occurrences = text;
            e.UpdatedAt = time.GetUtcNow().UtcDateTime;
        }
        await RevertControlEventsAsync(events, text, ct);
    }

    /// <summary>
    /// R-1000/R-2099/R-2098 que não chegaram à Receita (lote recusado ou falha de envio) viram Rejeitados e
    /// devolvem o período/cadastro à situação anterior; são regerados no próximo envio.
    /// </summary>
    private async Task RevertControlEventsAsync(IEnumerable<ReinfEvent> events, string text, CancellationToken ct)
    {
        foreach (var e in events.Where(x => !ProducerTypes.Contains(x.Type)).ToList())
        {
            e.Status = ReinfEventStatus.Rejected;
            e.Occurrences = text;
            e.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await OnRejectedAsync(e, ct);
        }
    }

    private async Task ApplyProcessedAsync(ReinfBatch batch, IReadOnlyList<ReinfEvent> events, ReinfBatchResult result, CancellationToken ct)
    {
        batch.Status = ReinfBatchStatus.Processed;
        batch.ProcessedAt = time.GetUtcNow().UtcDateTime;
        var nowUtc = time.GetUtcNow().UtcDateTime;

        foreach (var r in result.Events)
        {
            var e = events.FirstOrDefault(x => x.EventId == r.EventId);
            if (e is null) continue;
            e.UpdatedAt = nowUtc;
            e.Occurrences = r.Occurrences.Count > 0 ? string.Join(" | ", r.Occurrences) : null;
            if (!r.Accepted)
            {
                e.Status = ReinfEventStatus.Rejected;
                await OnRejectedAsync(e, ct);
                continue;
            }
            e.Status = ReinfEventStatus.Accepted;
            e.Receipt = r.Receipt;
            await OnAcceptedAsync(e, ct);
        }

        foreach (var e in events.Where(x => x.Status == ReinfEventStatus.Sent))
        {
            e.Status = ReinfEventStatus.Rejected;
            e.Occurrences = MissingFromResponseMessage;
            e.UpdatedAt = nowUtc;
            await OnRejectedAsync(e, ct);
        }
    }

    public const string MissingFromResponseMessage = "Evento ausente no retorno do lote.";

    private async Task OnAcceptedAsync(ReinfEvent e, CancellationToken ct)
    {
        switch (e.Type)
        {
            case ReinfEventType.R2055 when e.RectificationIndicator == 2 && e.PreviousReceipt is not null:
                foreach (var old in await TargetsAsync(e, ct)) old.Status = ReinfEventStatus.Outdated;
                break;
            case ReinfEventType.R9000 when e.PreviousReceipt is not null:
                foreach (var old in await TargetsAsync(e, ct)) old.Status = ReinfEventStatus.Excluded;
                break;
            case ReinfEventType.R2099:
            {
                var p = await PeriodAsync(e, ct);
                p.Status = ReinfPeriodStatus.Closed;
                p.ClosingReceipt = e.Receipt;
                p.ClosedAt = time.GetUtcNow().UtcDateTime;
                break;
            }
            case ReinfEventType.R2098:
            {
                var p = await PeriodAsync(e, ct);
                p.Status = ReinfPeriodStatus.Open;
                p.ClosingReceipt = null;
                p.ClosedAt = null;
                break;
            }
            case ReinfEventType.R1000:
            {
                var s = await context.ReinfSettings.FirstOrDefaultAsync(x => x.TaxIdRoot == e.TaxIdRoot, ct);
                if (s is not null)
                {
                    s.RegistrationStatus = ReinfRegistrationStatus.Accepted;
                    s.RegistrationReceipt = e.Receipt;
                    s.RegistrationUpdatePending = false;
                }
                break;
            }
        }
    }

    private async Task OnRejectedAsync(ReinfEvent e, CancellationToken ct)
    {
        switch (e.Type)
        {
            case ReinfEventType.R2099:
                (await PeriodAsync(e, ct)).Status = ReinfPeriodStatus.Open;
                break;
            case ReinfEventType.R2098:
                (await PeriodAsync(e, ct)).Status = ReinfPeriodStatus.Closed;
                break;
            case ReinfEventType.R1000:
                var s = await context.ReinfSettings.FirstOrDefaultAsync(x => x.TaxIdRoot == e.TaxIdRoot, ct);
                // Alteração recusada não invalida o cadastro já aceito (recibo anterior segue válido); as ocorrências ficam no evento.
                if (s is not null) s.RegistrationStatus = s.RegistrationReceipt is null ? ReinfRegistrationStatus.Rejected : ReinfRegistrationStatus.Accepted;
                break;
        }
    }

    private async Task<List<ReinfEvent>> TargetsAsync(ReinfEvent e, CancellationToken ct) =>
        await context.ReinfEvents
            .Where(x => x.Type == ReinfEventType.R2055 && x.TaxIdRoot == e.TaxIdRoot && x.Receipt == e.PreviousReceipt && x.Key != e.Key)
            .ToListAsync(ct);

    private async Task<ReinfPeriod> PeriodAsync(ReinfEvent e, CancellationToken ct)
    {
        var p = await context.ReinfPeriods.FirstOrDefaultAsync(x => x.TaxIdRoot == e.TaxIdRoot && x.Period == e.Period, ct);
        if (p is null)
        {
            p = new ReinfPeriod { TaxIdRoot = e.TaxIdRoot, Period = e.Period ?? string.Empty };
            context.ReinfPeriods.Add(p);
        }
        return p;
    }

    private static void Fill2055(ReinfEvent e, ReinfAcquisitionRow row, int retif, string? receipt)
    {
        e.Type = ReinfEventType.R2055;
        e.EstablishmentTaxId = row.EstablishmentTaxId;
        e.CardCode = row.CardCode;
        e.ProducerTaxId = row.ProducerTaxId;
        e.RectificationIndicator = retif;
        e.PreviousReceipt = receipt;
        e.GrossValue = row.Gross;
        e.InssValue = row.Inss;
        e.RatValue = row.Rat;
        e.SenarValue = row.Senar;
        e.ContentHash = row.ContentHash;
    }

    /// <summary>
    /// Espelha o plano de <see cref="TransmitCoreAsync"/>: "Rejeitado" só quando a rejeição é mais nova que o aceite e
    /// o envio ainda agiria na linha; senão a situação é calculada como se a rejeição não existisse.
    /// </summary>
    private static ReinfOverviewRow Situation(ReinfAcquisitionRow? row, ProducerState s)
    {
        var gross = row?.Gross ?? 0m;
        if (s.Unconfirmed is { } u) return new(row, u.Key, "Verificar", s.Accepted?.Receipt, UnknownOutcomeMessage);
        if (s.InFlight)
            return new(row, s.Latest?.Key, "Enviado", s.Accepted?.Receipt, s.Stale ? StaleBatchMessage : null);

        ReinfOverviewRow basis;
        bool acts;
        if (s.Accepted is { } a)
        {
            var situation = gross <= 0 ? "Excluir" : a.ContentHash == row!.ContentHash ? "Aceito" : "Desatualizado";
            acts = situation != "Aceito";
            basis = new(row, a.Key, situation, a.Receipt, a.Occurrences);
        }
        else if (gross <= 0)
        {
            acts = false;
            basis = s.Excluded is { } x
                ? new(row, x.Key, "Excluído", x.Receipt, null)
                : new(row, null, "Sem movimento", null, null);
        }
        else
        {
            acts = true;
            basis = new(row, s.Latest?.Key, "Novo", null, s.Latest?.Status == ReinfEventStatus.Rejected ? null : s.Latest?.Occurrences);
        }

        if (acts && s.Latest is { Status: ReinfEventStatus.Rejected } rejected
            && (s.Accepted is null || rejected.CreatedAt >= s.Accepted.CreatedAt))
            return new(row, rejected.Key, "Rejeitado", s.Accepted?.Receipt, rejected.Occurrences);
        return basis;
    }

    private void EnsureStandalone()
    {
        if (!ErpMode.IsStandalone(configuration)) throw new BusinessException(StandaloneOnlyMessage);
    }

    private DateTime BrasiliaNow() => TimeZoneInfo.ConvertTime(time.GetUtcNow(), NfeIssueInputAssembler.BrasiliaZone).DateTime;

    private async Task<PeriodState> LoadStateAsync(string root, string period, CancellationToken ct, bool tracked = false)
    {
        var query = context.ReinfEvents.Where(e => e.TaxIdRoot == root && e.Period == period && ProducerTypes.Contains(e.Type));
        var events = tracked ? await query.ToListAsync(ct) : await query.AsNoTracking().ToListAsync(ct);
        var batchKeys = events.Where(e => e.BatchKey != null).Select(e => e.BatchKey!.Value).Distinct().ToArray();
        var batches = await context.ReinfBatches.AsNoTracking().Where(b => batchKeys.Contains(b.Key))
            .ToDictionaryAsync(b => b.Key,
                b => (Status: b.Status == ReinfBatchStatus.Sent && string.IsNullOrWhiteSpace(b.Protocol) ? ReinfBatchStatus.Failed : b.Status,
                    b.Attempts), ct);
        return new PeriodState(events, batches);
    }

    private sealed class PeriodState(List<ReinfEvent> events, Dictionary<Guid, (ReinfBatchStatus Status, int Attempts)> batches)
    {
        public ProducerState For(string? establishment, string? cardCode)
        {
            var mine = events.Where(e => e.EstablishmentTaxId == establishment && e.CardCode == cardCode)
                .OrderByDescending(e => e.CreatedAt).ToList();
            bool InFlight(ReinfEvent e) =>
                e.Status == ReinfEventStatus.Sent
                || (e.Status == ReinfEventStatus.Pending && e.BatchKey is { } k && batches.TryGetValue(k, out var st)
                    && st.Status is ReinfBatchStatus.Sent or ReinfBatchStatus.Processing);
            bool Stale(ReinfEvent e) => InFlight(e) && e.BatchKey is { } k && batches.TryGetValue(k, out var st)
                && st.Status is ReinfBatchStatus.Sent or ReinfBatchStatus.Processing && st.Attempts >= MaxAutomaticQueries;
            return new ProducerState(
                mine.FirstOrDefault(e => e.Status != ReinfEventStatus.Outdated),
                mine.FirstOrDefault(e => e.Type == ReinfEventType.R2055 && e.Status == ReinfEventStatus.Accepted),
                mine.FirstOrDefault(e => e.Status == ReinfEventStatus.Excluded),
                mine.FirstOrDefault(e => e.Status == ReinfEventStatus.Pending && !InFlight(e)),
                mine.Any(InFlight),
                mine.FirstOrDefault(e => e.Status == ReinfEventStatus.Unconfirmed),
                mine.Any(Stale));
        }

        /// <summary>Produtores com R-2055 aceito que não aparecem mais na apuração.</summary>
        public IEnumerable<(string? Establishment, string? CardCode)> OrphanKeys(IReadOnlyList<ReinfAcquisitionRow> rows)
        {
            var present = rows.Select(r => (r.EstablishmentTaxId, r.CardCode)).ToHashSet();
            return events.Where(e => e.Type == ReinfEventType.R2055 && e.Status == ReinfEventStatus.Accepted)
                .Select(e => (e.EstablishmentTaxId, e.CardCode))
                .Where(k => !present.Contains((k.EstablishmentTaxId!, k.CardCode!)))
                .Distinct()
                .Select(k => ((string?)k.EstablishmentTaxId, (string?)k.CardCode));
        }
    }

    private sealed record ProducerState(ReinfEvent? Latest, ReinfEvent? Accepted, ReinfEvent? Excluded, ReinfEvent? Retryable, bool InFlight,
        ReinfEvent? Unconfirmed, bool Stale);
}
