using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.FreightOrders;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Ganchos reais do ciclo de vida do CT-e (só STANDALONE): na autorização gera o título a receber contra o tomador; no
/// cancelamento cancela o título e a averbação. A trava de título baixado roda ANTES da SEFAZ, no
/// <see cref="TransportDocumentsCancelService"/>.
/// </summary>
public class CteLifecycleHooks(
    IUnitOfWork db,
    IConfiguration configuration,
    FinancialDocumentsTransportService financial,
    CteEndorsementCancellationService endorsementCancellation,
    ILogger<CteLifecycleHooks> logger,
    INfeEmailOutboxService? emailOutbox = null,
    FreightOrdersRecalculateService? freightOrders = null) : ICteAuthorizationHook, ICteCancellationHook
{
    /// <summary>
    /// Não lança: o CT-e já está autorizado na SEFAZ. Uma falha fica no log (o serviço é idempotente: um título já
    /// existente nunca é duplicado).
    /// </summary>
    public async Task OnAuthorizedAsync(TransportDocument doc)
    {
        if (!ErpMode.IsStandalone(configuration)) return;
        try
        {
            var parties = db.Context.Entry(doc).Collection(x => x.Parties);
            if (!parties.IsLoaded)
                await parties.LoadAsync();
            await financial.EnqueueAsync(doc, User(doc));
            if (doc.FinancialError is not null && (await financial.ActiveTitleCodesAsync(doc.Key)).Count > 0)
                doc.FinancialError = null;
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "CT-e {AccessKey} autorizado, mas o título a receber não foi gerado.", doc.AccessKey);
            await RecordFinancialErrorAsync(doc, e);
        }

        await RecalculateFreightOrderAsync(doc);
        await EnqueueEmailAsync(doc, NfeEmailEventType.Authorized);
    }

    /// <summary>
    /// Atualiza o saldo da ordem de frete do CT-e (os chamadores já gravaram a situação do CT-e). Nunca lança.
    /// </summary>
    private async Task RecalculateFreightOrderAsync(TransportDocument doc)
    {
        if (freightOrders is null || doc.FreightOrderKey is not { } orderKey) return;
        try
        {
            await freightOrders.RecalculateAsync(orderKey);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CT-e {AccessKey}: falha ao recalcular o saldo da ordem de frete.", doc.AccessKey);
        }
    }

    /// <summary>
    /// E-mail automático (XML + DACTE). Nunca lança: o CT-e já está na SEFAZ. O outbox deduplica por documento e evento,
    /// então a consulta repetida não enfileira de novo.
    /// </summary>
    private async Task EnqueueEmailAsync(TransportDocument doc, NfeEmailEventType evt)
    {
        if (emailOutbox is null || !ErpMode.IsStandalone(configuration)) return;
        try
        {
            await emailOutbox.AddAutomaticAsync(NfeEmailDocumentType.Transport, doc.Key, evt, User(doc));
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "CT-e {AccessKey}: falha ao enfileirar o e-mail ({EventType}).", doc.AccessKey, evt);
            foreach (var entry in db.Context.ChangeTracker.Entries<NfeEmailMessage>()
                         .Where(x => x.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Deixa a falha visível no documento (o botão "Gerar título" e a próxima consulta tentam de novo). Os títulos que
    /// ficaram pendurados no contexto são descartados antes, para não irem junto num SaveChanges posterior.
    /// </summary>
    private async Task RecordFinancialErrorAsync(TransportDocument doc, Exception error, string prefix = "Título a receber não gerado")
    {
        try
        {
            foreach (var entry in db.Context.ChangeTracker.Entries<FinancialDocument>()
                         .Where(x => x.State is EntityState.Added or EntityState.Modified).ToList())
            {
                if (entry.State == EntityState.Added)
                    entry.State = EntityState.Detached;
                else
                    await entry.ReloadAsync();
            }

            var reason = $"{prefix}: {(error is DefaultException ? error.Message : "erro inesperado; veja o log.")}";
            doc.FinancialError = reason.Length > 500 ? reason[..500] : reason;
            await db.SaveChangesAsync();
        }
        catch (Exception inner)
        {
            logger.LogError(inner, "Não foi possível gravar o motivo da falha do título do CT-e {AccessKey}.", doc.AccessKey);
        }
    }

    /// <summary>Não lança: o CT-e já está cancelado na SEFAZ (a baixa foi recusada antes, no serviço de cancelamento).</summary>
    public async Task OnCancelledAsync(TransportDocument doc)
    {
        if (!ErpMode.IsStandalone(configuration)) return;
        try
        {
            financial.EnqueueUndo(doc.Key, User(doc));
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "CT-e {AccessKey} cancelado, mas o título a receber não foi cancelado.", doc.AccessKey);
            // Ex.: título baixado no Financeiro durante a ida à SEFAZ. O aviso fica no documento para o usuário resolver.
            await RecordFinancialErrorAsync(doc, e, "Título a receber não cancelado");
        }

        await RecalculateFreightOrderAsync(doc);
        await EnqueueEmailAsync(doc, NfeEmailEventType.Cancelled);
        await endorsementCancellation.CancelAsync(doc);
    }

    private static string User(TransportDocument doc) => doc.UpdatedBy ?? doc.CreatedBy ?? "Sistema";
}
