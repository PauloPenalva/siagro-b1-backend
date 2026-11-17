using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>Situação de uma A/R Invoice no SAP (OINV) lida pela sincronização.</summary>
public class SapInvoiceStatus
{
    public int DocEntry { get; set; }
    public int? BranchId { get; set; }

    /// <summary>OINV.CANCELED: 'Y' cancelada, 'N' normal, 'C' é o documento de cancelamento.</summary>
    public string? Canceled { get; set; }
}

/// <summary>
/// Acompanha, no Siagro, o cancelamento no SAP das invoices vinculadas a documentos de saída (GAC-1226) e
/// preenche a chave de acesso dos documentos vinculados que nasceram sem ela. Só em SAPB1.
/// </summary>
/// <remarks>
/// O Hangfire dispara o job a cada minuto, por empresa; quem decide se o intervalo da empresa venceu é este
/// serviço (<see cref="SapIntegrationSettings"/>). Cada documento é tratado em escopo de DI PRÓPRIO: um
/// cancelamento que falha no meio deixa o contexto com entidades sujas e ele não pode ser reaproveitado.
/// <para>
/// Armadilha: escopo novo do <see cref="IServiceScopeFactory"/> NÃO herda a empresa do escopo da requisição (no
/// HTTP ela é só explícita, sem <see cref="CompanyAmbient"/>) e cairia no banco padrão. Todo escopo filho passa por
/// <see cref="RunInCompanyScopeAsync"/>, que repete a empresa (mesmo padrão do CompanyScopedJobRunner/CmaCompanyScope).
/// </para>
/// </remarks>
public class SapInvoiceSyncService(
    AppDbContext context,
    SapErpDbContext sap,
    ISapFiscalDocumentSource fiscal,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<SapInvoiceSyncService> logger,
    ICompanyContext companyContext,
    ISapInvoiceSyncLock syncLock)
{
    /// <summary>Resposta do "Sincronizar agora" quando outra execução da mesma empresa está em andamento.</summary>
    public const string BusyMessage = "Já existe uma sincronização em andamento. Tente novamente em instantes.";

    /// <summary>Resposta do "Sincronizar agora" quando a trava da empresa não pôde nem ser pedida (banco fora do ar).</summary>
    public const string LockUnavailableMessage = "Não foi possível iniciar a sincronização. Tente novamente em instantes.";

    /// <summary>Usuário gravado nos cancelamentos feitos pela sincronização.</summary>
    public const string SyncUser = "sap-sync";

    /// <summary>Motivo gravado no log da devolução quando a sincronização a desvincula.</summary>
    public const string CreditMemoCancelledReason = "NF de devolução cancelada no SAP.";

    /// <summary>Tamanho do lote de DocEntry nas consultas (limite de parâmetros).</summary>
    private const int ChunkSize = 1_000;

    private const int ReasonMaxLength = 500;
    private const int AccessKeyLength = 44;

    /// <summary>Empresa da execução corrente, repetida em cada escopo filho.</summary>
    private CompanyInfo _company = null!;

    private sealed record LinkedInvoice(Guid Key, int DocEntry, InvoiceStatus Status, string? AccessKey, string? BranchCode);

    /// <param name="ct">Cancelamento.</param>
    /// <param name="force">"Sincronizar agora": ignora o liga/desliga e o intervalo.</param>
    public async Task ExecuteAsync(CancellationToken ct, bool force = false)
    {
        // Sem empresa resolvida os escopos filhos cancelariam no banco padrão: melhor falhar.
        _company = companyContext.Current
                   ?? throw new InvalidOperationException(
                       "Sincronização das invoices do SAP sem empresa definida: não pode rodar no banco padrão.");

        // Manual x agendado da mesma empresa nunca ao mesmo tempo. O agendado volta no próximo minuto.
        IAsyncDisposable? acquired;
        try
        {
            acquired = await syncLock.TryAcquireAsync(_company.Code, ct);
        }
        catch (Exception e) when (e is DbException or TimeoutException)
        {
            // Banco da empresa fora do ar ao pedir a trava: o agendado pula sem carimbar a última execução (o
            // próximo minuto tenta de novo); o "Sincronizar agora" recebe mensagem de negócio, não o erro cru.
            logger.LogWarning(e, "Sincronização das invoices do SAP da empresa {Company}: trava indisponível; pulada.",
                _company.Code);
            if (force)
                throw new DefaultException(LockUnavailableMessage);
            return;
        }

        await using var held = acquired;
        if (held is null)
        {
            if (force)
                throw new DefaultException(BusyMessage);

            logger.LogInformation("Sincronização das invoices do SAP da empresa {Company} já em andamento; pulada.",
                _company.Code);
            return;
        }

        var settings = await context.SapIntegrationSettings.FirstOrDefaultAsync(ct);
        var now = clock.GetLocalNow().DateTime;

        if (!force && !IsDue(settings, now))
            return;

        var linked = await LoadLinkedInvoicesAsync(ct);
        var linkedCreditMemos = await LoadLinkedCreditMemosAsync(ct);

        var sapRows = new Dictionary<int, SapInvoiceStatus>();
        var infos = new Dictionary<int, SapFiscalDocumentInfo>();

        // NF de devolução (A/R Credit Memo): dicionários PRÓPRIOS. O DocEntry de ORIN é outra sequência e o mesmo
        // número em OINV é outro documento — misturar faria a invoice cancelada responder pela devolução.
        var creditMemoRows = new Dictionary<int, SapInvoiceStatus>();
        var creditMemoInfos = new Dictionary<int, SapFiscalDocumentInfo>();

        try
        {
            foreach (var chunk in linkedCreditMemos.Select(x => x.DocEntry).Distinct().Chunk(ChunkSize))
                foreach (var row in await ReadSapCreditMemoChunkAsync(chunk, ct))
                    creditMemoRows[row.DocEntry] = row;

            if (fiscal.IsAvailable)
                foreach (var chunk in creditMemoRows.Values.Chunk(ChunkSize))
                {
                    var found = await fiscal.GetForCreditMemosAsync(
                        chunk.Select(r => new SapInvoiceRef(r.DocEntry, r.BranchId)).ToList(), ct);
                    foreach (var (docEntry, info) in found)
                        creditMemoInfos[docEntry] = info;
                }

            foreach (var chunk in linked.Select(x => x.DocEntry).Distinct().Chunk(ChunkSize))
                foreach (var row in await ReadSapChunkAsync(chunk, ct))
                    sapRows[row.DocEntry] = row;

            if (fiscal.IsAvailable)
                foreach (var chunk in sapRows.Values.Chunk(ChunkSize))
                {
                    var found = await fiscal.GetForInvoicesAsync(
                        chunk.Select(r => new SapInvoiceRef(r.DocEntry, r.BranchId)).ToList(), ct);
                    foreach (var (docEntry, info) in found)
                        infos[docEntry] = info;
                }
        }
        catch (Exception e) when (e is DbException or TimeoutException)
        {
            // Sem carimbar a última execução: o próximo minuto tenta de novo.
            logger.LogWarning(e, "Sincronização das invoices do SAP adiada: SAP ou add-on fiscal indisponível.");
            return;
        }

        foreach (var item in linked)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await SyncOneAsync(item, sapRows, infos, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Um documento com problema não pode parar os demais.
                logger.LogError(e, "Falha ao sincronizar o documento de saída {Key} (invoice SAP {DocEntry}).",
                    item.Key, item.DocEntry);
            }
        }

        foreach (var item in linkedCreditMemos)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await SyncCreditMemoAsync(item, creditMemoRows, creditMemoInfos, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Falha ao sincronizar a devolução {Key} (NF de devolução SAP {DocEntry}).",
                    item.Key, item.DocEntry);
            }
        }

        if (settings is null)
        {
            settings = new SapIntegrationSettings();
            context.SapIntegrationSettings.Add(settings);
        }

        settings.InvoiceSyncLastRunAt = now;
        await context.SaveChangesAsync(ct);
    }

    /// <summary>Lê a situação das invoices no SAP. Costura de teste: permite simular o SAP fora do ar.</summary>
    protected virtual Task<List<SapInvoiceStatus>> ReadSapChunkAsync(int[] docEntries, CancellationToken ct) =>
        sap.SapInvoices.AsNoTracking()
            .Where(i => docEntries.Contains(i.DocEntry))
            .Select(i => new SapInvoiceStatus { DocEntry = i.DocEntry, BranchId = i.BPLId, Canceled = i.Canceled })
            .ToListAsync(ct);

    /// <summary>Lê a situação das NF de devolução (ORIN) no SAP. Costura de teste, como a das invoices.</summary>
    protected virtual Task<List<SapInvoiceStatus>> ReadSapCreditMemoChunkAsync(int[] docEntries, CancellationToken ct) =>
        sap.SapCreditMemos.AsNoTracking()
            .Where(c => docEntries.Contains(c.DocEntry))
            .Select(c => new SapInvoiceStatus { DocEntry = c.DocEntry, BranchId = c.BPLId, Canceled = c.Canceled })
            .ToListAsync(ct);

    /// <summary>Devoluções confirmadas com NF de devolução do SAP vinculada (GAC-1226).</summary>
    private async Task<List<LinkedInvoice>> LoadLinkedCreditMemosAsync(CancellationToken ct)
    {
        var rows = await context.SalesInvoices.AsNoTracking()
            .Where(x => x.SapCreditMemoDocEntry != null && x.InvoiceStatus == InvoiceStatus.Confirmed)
            .OrderBy(x => x.SapCreditMemoDocEntry)
            .Select(x => new { x.Key, DocEntry = x.SapCreditMemoDocEntry!.Value, x.InvoiceStatus, x.ChaveNFe, x.BranchCode })
            .ToListAsync(ct);

        return rows.Select(x =>
            new LinkedInvoice(x.Key, x.DocEntry, x.InvoiceStatus!.Value, x.ChaveNFe, x.BranchCode)).ToList();
    }

    /// <summary>
    /// NF de devolução cancelada no SAP: a devolução é só DESVINCULADA e volta a aguardar a nota. Nunca é cancelada —
    /// o físico dela (saldo da carga, armazém, contrato) já aconteceu e não depende da nota.
    /// </summary>
    private async Task SyncCreditMemoAsync(
        LinkedInvoice item,
        IReadOnlyDictionary<int, SapInvoiceStatus> sapRows,
        IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos,
        CancellationToken ct)
    {
        // Ausente do SAP NÃO é cancelamento (base errada, filtro de empresa...): só avisa.
        if (!sapRows.TryGetValue(item.DocEntry, out var row))
        {
            logger.LogWarning("NF de devolução {DocEntry} da devolução {Key} não foi encontrada no SAP.",
                item.DocEntry, item.Key);
            return;
        }

        if (!string.Equals(row.BranchId?.ToString(), item.BranchCode?.Trim(), StringComparison.Ordinal))
        {
            logger.LogWarning(
                "NF de devolução {DocEntry} do SAP é da filial {SapBranch}, mas a devolução {Key} é da filial {Branch}; ignorada.",
                item.DocEntry, row.BranchId, item.Key, item.BranchCode);
            return;
        }

        infos.TryGetValue(item.DocEntry, out var info);

        if (row.Canceled == "Y" || info?.Cancelled == true)
        {
            await RunInCompanyScopeAsync(sp => sp.GetRequiredService<ShipmentLoadsUnlinkSapCreditMemoService>()
                .ExecuteAsync(null, item.Key, SyncUser, CreditMemoCancelledReason));
            return;
        }

        if (item.AccessKey is null && !string.IsNullOrWhiteSpace(info?.AccessKey))
            await BackfillAccessKeyAsync(item, info.AccessKey.Trim(), ct);
    }

    private static bool IsDue(SapIntegrationSettings? settings, DateTime now)
    {
        // Sem linha valem os padrões da entidade.
        settings ??= new SapIntegrationSettings();

        if (!settings.InvoiceSyncEnabled)
            return false;

        return settings.InvoiceSyncLastRunAt is not { } last
               || now - last >= TimeSpan.FromMinutes(settings.InvoiceSyncIntervalMinutes);
    }

    /// <summary>Documentos vinculados vivos (Pendente ou Confirmado), inclusive os já marcados como pendentes.</summary>
    private async Task<List<LinkedInvoice>> LoadLinkedInvoicesAsync(CancellationToken ct)
    {
        var rows = await context.SalesInvoices.AsNoTracking()
            .Where(x => x.SapDocEntry != null
                        && (x.InvoiceStatus == InvoiceStatus.Pending || x.InvoiceStatus == InvoiceStatus.Confirmed))
            .OrderBy(x => x.SapDocEntry)
            .Select(x => new { x.Key, DocEntry = x.SapDocEntry!.Value, x.InvoiceStatus, x.ChaveNFe, x.BranchCode })
            .ToListAsync(ct);

        return rows.Select(x =>
            new LinkedInvoice(x.Key, x.DocEntry, x.InvoiceStatus!.Value, x.ChaveNFe, x.BranchCode)).ToList();
    }

    private async Task SyncOneAsync(
        LinkedInvoice item,
        IReadOnlyDictionary<int, SapInvoiceStatus> sapRows,
        IReadOnlyDictionary<int, SapFiscalDocumentInfo> infos,
        CancellationToken ct)
    {
        // Invoice ausente do SAP NÃO é cancelamento (base errada, filtro de empresa...): só avisa.
        if (!sapRows.TryGetValue(item.DocEntry, out var row))
        {
            logger.LogWarning("Invoice {DocEntry} do documento de saída {Key} não foi encontrada no SAP.",
                item.DocEntry, item.Key);
            return;
        }

        // Seguro barato: a linha do SAP só é deste documento se a filial bate (OINV.BPLId é o Branch.Code, como
        // texto). Filial diferente com o mesmo DocEntry = base ou vínculo errado: não cancela nada, só avisa.
        if (!string.Equals(row.BranchId?.ToString(), item.BranchCode?.Trim(), StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Invoice {DocEntry} do SAP é da filial {SapBranch}, mas o documento de saída {Key} é da filial {Branch}; ignorada.",
                item.DocEntry, row.BranchId, item.Key, item.BranchCode);
            return;
        }

        infos.TryGetValue(item.DocEntry, out var info);

        if (row.Canceled == "Y" || info?.Cancelled == true)
        {
            await FollowSapCancellationAsync(item, ct);
            return;
        }

        if (item.AccessKey is null && !string.IsNullOrWhiteSpace(info?.AccessKey))
            await BackfillAccessKeyAsync(item, info.AccessKey.Trim(), ct);
    }

    private async Task FollowSapCancellationAsync(LinkedInvoice item, CancellationToken ct)
    {
        try
        {
            // O cancelamento limpa a pendência por conta própria; a exclusão leva o documento junto.
            await RunInCompanyScopeAsync(sp => item.Status == InvoiceStatus.Confirmed
                ? sp.GetRequiredService<SalesInvoicesCancelService>().ExecuteAsync(item.Key, SyncUser)
                : sp.GetRequiredService<SalesInvoicesDeleteService>().ExecuteAsync(item.Key, SyncUser));
        }
        catch (Exception e) when (e is DefaultException or BusinessException or ApplicationException)
        {
            // Recusa de regra de negócio (título baixado, devolução...): o documento fica intacto e a
            // tentativa se repete na próxima rodada. O contexto do cancelamento está sujo: escopo novo.
            await MarkCancelPendingAsync(item.Key, e.Message, ct);
        }
    }

    /// <summary>Escopo de DI novo na empresa da execução: ambiente + explícita, como no CompanyScopedJobRunner.</summary>
    private async Task RunInCompanyScopeAsync(Func<IServiceProvider, Task> work)
    {
        using var _ = CompanyAmbient.Use(_company.Code);
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(_company);
        await work(scope.ServiceProvider);
    }

    private Task MarkCancelPendingAsync(Guid key, string reason, CancellationToken ct) =>
        RunInCompanyScopeAsync(sp => MarkCancelPendingAsync(sp.GetRequiredService<AppDbContext>(), key, reason, ct));

    private async Task MarkCancelPendingAsync(AppDbContext db, Guid key, string reason, CancellationToken ct)
    {
        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Key == key, ct);

        // Já cancelado/devolvido por outro caminho no meio da rodada: nada a sinalizar.
        if (invoice is null || invoice.InvoiceStatus is not (InvoiceStatus.Pending or InvoiceStatus.Confirmed))
            return;

        invoice.SapCancelPending = true;
        invoice.SapCancelPendingReason = reason.Length > ReasonMaxLength ? reason[..ReasonMaxLength] : reason;
        // A primeira detecção é a que vale para o usuário, mesmo que a recusa se repita.
        invoice.SapCancelDetectedAt ??= clock.GetLocalNow().DateTime;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Grava a chave do add-on no documento vinculado que nasceu sem ela. Escrita direta do serviço: a trava
    /// <c>SalesInvoiceNfeLock.RestoreIssuanceFields</c> só age no PATCH.
    /// </summary>
    private async Task BackfillAccessKeyAsync(LinkedInvoice item, string accessKey, CancellationToken ct)
    {
        if (accessKey.Length != AccessKeyLength || !accessKey.All(char.IsAsciiDigit))
        {
            logger.LogWarning("Chave de acesso do add-on inválida para a invoice {DocEntry}; ignorada.", item.DocEntry);
            return;
        }

        await RunInCompanyScopeAsync(sp => BackfillAccessKeyAsync(sp, item, accessKey, ct));
    }

    private async Task BackfillAccessKeyAsync(IServiceProvider sp, LinkedInvoice item, string accessKey, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();

        var invoice = await db.SalesInvoices.FirstOrDefaultAsync(x => x.Key == item.Key, ct);

        if (invoice is null || invoice.ChaveNFe is not null)
            return;

        try
        {
            await sp.GetRequiredService<SalesInvoiceTaxDocumentDuplicateGuard>()
                .EnsureUniqueAsync(invoice.BranchCode ?? string.Empty, null, null, accessKey, invoice.Key);
        }
        catch (ApplicationException e)
        {
            logger.LogWarning("Chave da invoice {DocEntry} não gravada no documento {Key}: {Reason}",
                item.DocEntry, item.Key, e.Message);
            return;
        }

        invoice.ChaveNFe = accessKey;
        await db.SaveChangesAsync(ct);
    }
}
