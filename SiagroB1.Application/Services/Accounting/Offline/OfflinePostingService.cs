using System.Globalization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Services.Accounting.Offline;

public interface IOfflinePostingService
{
    /// <summary>Valida, grava a execução como <see cref="OfflinePostingRunStatus.Queued"/> e enfileira o job.</summary>
    Task<OfflinePostingRun> RequestAsync(DateTime from, DateTime to, string? branchCode,
        IReadOnlyList<OfflinePostingModule> modules, bool simulation, string user, CancellationToken ct);

    /// <summary>Roda a execução (chamado pelo job): um documento por transação, relatório em OFFLINE_POSTING_RUN_ITEMS.</summary>
    Task ExecuteAsync(Guid runKey, CancellationToken ct);
}

/// <summary>
/// Contabilização off-line (CTBANFE / CTBANFS / FINA370): percorre em ordem cronológica os eventos dos coletores e
/// contabiliza cada documento com o motor do on-line. Validação é toda do motor; aqui só se isola cada documento.
/// </summary>
public class OfflinePostingService(
    IUnitOfWork db,
    IConfiguration configuration,
    IAccountingSettingsService settings,
    IEnumerable<IOfflinePostingSource> sources,
    IAccountingPostingService engine,
    IBackgroundJobClient jobs,
    ICompanyContext company) : IOfflinePostingService
{
    public async Task<OfflinePostingRun> RequestAsync(DateTime from, DateTime to, string? branchCode,
        IReadOnlyList<OfflinePostingModule> modules, bool simulation, string user, CancellationToken ct)
    {
        if (!ErpMode.IsStandalone(configuration)) throw new DefaultException("Disponível somente no modo STANDALONE.");
        var cutover = (await settings.GetAsync()).CutoverDate
                      ?? throw new DefaultException("Defina a data de corte da contabilidade antes da contabilização off-line.");
        if (from.Date > to.Date) throw new DefaultException("A data inicial deve ser menor ou igual à data final.");
        if (modules.Count == 0) throw new DefaultException("Escolha ao menos um módulo.");
        if (await db.Context.OfflinePostingRuns.AnyAsync(
                r => r.Status == OfflinePostingRunStatus.Queued || r.Status == OfflinePostingRunStatus.Running, ct))
            throw new DefaultException("Já existe uma contabilização off-line em andamento.");

        var start = from.Date < cutover.Date ? cutover.Date : from.Date;
        var run = new OfflinePostingRun
        {
            DateFrom = start,
            DateTo = to.Date,
            BranchCode = string.IsNullOrWhiteSpace(branchCode) ? null : branchCode.Trim(),
            Modules = string.Join(",", modules.Distinct().OrderBy(m => m).Select(m => ((int)m).ToString(CultureInfo.InvariantCulture))),
            Simulation = simulation,
            RequestedBy = user,
            RequestedAt = DateTime.Now,
        };
        db.Context.OfflinePostingRuns.Add(run);
        await db.SaveChangesAsync();

        // Depois do SaveChanges: o job não pode achar a execução ainda não gravada.
        var companyCode = company.CompanyCode;
        var key = run.Key;
        jobs.Enqueue<OfflinePostingJob>(j => j.ExecuteAsync(companyCode, key, CancellationToken.None));
        return run;
    }

    public async Task ExecuteAsync(Guid runKey, CancellationToken ct)
    {
        var run = await db.Context.OfflinePostingRuns.FirstAsync(r => r.Key == runKey, ct);
        run.Status = OfflinePostingRunStatus.Running;
        run.StartedAt = DateTime.Now;
        await db.SaveChangesAsync();

        int posted = 0, errors = 0, skipped = 0;
        try
        {
            var modules = run.Modules.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(m => (OfflinePostingModule)int.Parse(m, CultureInfo.InvariantCulture)).ToHashSet();
            var events = new List<OfflinePostingEvent>();
            foreach (var source in sources.Where(s => modules.Contains(s.Module)))
                await foreach (var e in source.CollectAsync(run.DateFrom, run.DateTo, run.BranchCode, ct))
                    events.Add(e);

            var sequence = 0;
            foreach (var e in events.OrderBy(e => e.Date).ThenBy(e => e.DocumentType, StringComparer.Ordinal)
                         .ThenBy(e => e.DocumentNumber, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                db.Context.ChangeTracker.Clear();
                var item = new OfflinePostingRunItem
                {
                    RunKey = runKey,
                    Sequence = ++sequence,
                    EventDate = e.Date,
                    DocumentType = e.DocumentType,
                    DocumentKey = e.DocumentKey,
                    DocumentNumber = Truncate(e.DocumentNumber, 60),
                    BranchCode = e.BranchCode,
                    Codes = string.Join(",", e.Codes),
                    Message = e.Note,
                };

                await ProcessAsync(e, item, run.Simulation, run.RequestedBy, ct);
                switch (item.Result)
                {
                    case OfflinePostingItemResult.Error: errors++; break;
                    case OfflinePostingItemResult.Skipped: skipped++; break;
                    default: posted++; break;
                }
            }

            db.Context.ChangeTracker.Clear();
            run = await db.Context.OfflinePostingRuns.FirstAsync(r => r.Key == runKey, ct);
            run.Status = OfflinePostingRunStatus.Completed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.Context.ChangeTracker.Clear();
            run = await db.Context.OfflinePostingRuns.FirstAsync(r => r.Key == runKey, CancellationToken.None);
            run.Status = OfflinePostingRunStatus.Failed;
            run.FailureMessage = Truncate(ex.Message, 2000);
        }

        run.PostedCount = posted;
        run.ErrorCount = errors;
        run.SkippedCount = skipped;
        run.FinishedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Um documento = uma transação: os lançamentos e a linha do relatório vão no mesmo SaveChanges. Na simulação e
    /// no erro, o que o motor enfileirou é descartado e só a linha do relatório é gravada.
    /// </summary>
    private async Task ProcessAsync(OfflinePostingEvent e, OfflinePostingRunItem item, bool simulation, string user,
        CancellationToken ct)
    {
        if (e.Codes.Count == 0)
        {
            item.Result = OfflinePostingItemResult.Skipped;
            await SaveItemAsync(item);
            return;
        }

        IReadOnlyList<JournalEntry> entries;
        try
        {
            entries = await e.EnqueueAsync(engine, user, ct);
            if (entries.Count == 0)
            {
                item.Result = OfflinePostingItemResult.Skipped;
                item.Message = Join(item.Message, "Sem valor a contabilizar");
                db.Context.ChangeTracker.Clear();
                await SaveItemAsync(item);
                return;
            }

            if (simulation)
            {
                item.Result = OfflinePostingItemResult.WouldPost;
                db.Context.ChangeTracker.Clear();
                await SaveItemAsync(item);
                return;
            }

            item.Result = OfflinePostingItemResult.Posted;
            db.Context.OfflinePostingRunItems.Add(item);
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.Context.ChangeTracker.Clear();
            item.Result = OfflinePostingItemResult.Error;
            var message = ex.Message.StartsWith("LP ", StringComparison.Ordinal) ? ex.Message : $"LP {string.Join("/", e.Codes)}: {ex.Message}";
            item.Message = Truncate(Join(item.Message, message), 2000);
            await SaveItemAsync(item);
            return;
        }

        item.JournalEntryDocEntries = Truncate(string.Join(",", entries.Select(x => x.DocEntry)), 200);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private async Task SaveItemAsync(OfflinePostingRunItem item)
    {
        db.Context.OfflinePostingRunItems.Add(item);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private static string? Join(string? first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first}; {second}";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
