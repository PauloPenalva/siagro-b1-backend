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

    public async Task<IReadOnlyList<Guid>> TransmitAsync(string taxIdRoot, string period, string user, CancellationToken ct)
    {
        EnsureStandalone();
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
        var now = BrasiliaNow();

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
            if (s.InFlight) continue;
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
            if (!s.InFlight && s.Accepted is not null) PlanExclusion(s.Accepted, s.Retryable);
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
                    if (result.ResponseCode is 2 or 3) await ApplyProcessedAsync(batch, events, result, ct);
                }
                else Refuse(batch, events, result);
            }
            catch (ReinfTransportException ex)
            {
                batch.Status = ReinfBatchStatus.Failed;
                batch.Error = $"HTTP {ex.StatusCode}: {ex.Body}";
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
            Refuse(batch, events, result);

        await context.SaveChangesAsync(ct);
    }

    public Task<Guid> SendRegistrationAsync(string taxIdRoot, string user, CancellationToken ct) => throw new NotImplementedException();
    public Task<Guid> CloseAsync(string taxIdRoot, string period, string user, CancellationToken ct) => throw new NotImplementedException();
    public Task<Guid> ReopenAsync(string taxIdRoot, string period, string user, CancellationToken ct) => throw new NotImplementedException();

    private void Refuse(ReinfBatch batch, IEnumerable<ReinfEvent> events, ReinfBatchResult result)
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
    }

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
                if (s is not null) { s.RegistrationStatus = ReinfRegistrationStatus.Accepted; s.RegistrationReceipt = e.Receipt; }
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
                if (s is not null) s.RegistrationStatus = ReinfRegistrationStatus.Rejected;
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

    private static ReinfOverviewRow Situation(ReinfAcquisitionRow? row, ProducerState s)
    {
        var gross = row?.Gross ?? 0m;
        if (s.InFlight) return new(row, s.Latest?.Key, "Enviado", s.Accepted?.Receipt, null);
        if (s.Latest is { Status: ReinfEventStatus.Rejected } rejected)
            return new(row, rejected.Key, "Rejeitado", s.Accepted?.Receipt, rejected.Occurrences);
        if (s.Accepted is { } a)
        {
            var situation = gross <= 0 ? "Excluir" : a.ContentHash == row!.ContentHash ? "Aceito" : "Desatualizado";
            return new(row, a.Key, situation, a.Receipt, a.Occurrences);
        }
        if (s.Excluded is { } x && gross <= 0) return new(row, x.Key, "Excluído", x.Receipt, null);
        return new(row, s.Latest?.Key, "Novo", null, s.Latest?.Occurrences);
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
            .ToDictionaryAsync(b => b.Key, b => b.Status, ct);
        return new PeriodState(events, batches);
    }

    private sealed class PeriodState(List<ReinfEvent> events, Dictionary<Guid, ReinfBatchStatus> batches)
    {
        public ProducerState For(string? establishment, string? cardCode)
        {
            var mine = events.Where(e => e.EstablishmentTaxId == establishment && e.CardCode == cardCode)
                .OrderByDescending(e => e.CreatedAt).ToList();
            bool InFlight(ReinfEvent e) =>
                e.Status == ReinfEventStatus.Sent
                || (e.Status == ReinfEventStatus.Pending && e.BatchKey is { } k && batches.TryGetValue(k, out var st)
                    && st is ReinfBatchStatus.Sent or ReinfBatchStatus.Processing);
            return new ProducerState(
                mine.FirstOrDefault(e => e.Status != ReinfEventStatus.Outdated),
                mine.FirstOrDefault(e => e.Type == ReinfEventType.R2055 && e.Status == ReinfEventStatus.Accepted),
                mine.FirstOrDefault(e => e.Status == ReinfEventStatus.Excluded),
                mine.FirstOrDefault(e => e.Status == ReinfEventStatus.Pending && !InFlight(e)),
                mine.Any(InFlight));
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

    private sealed record ProducerState(ReinfEvent? Latest, ReinfEvent? Accepted, ReinfEvent? Excluded, ReinfEvent? Retryable, bool InFlight);
}
