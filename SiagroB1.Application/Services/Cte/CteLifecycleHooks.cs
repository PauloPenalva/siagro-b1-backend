using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
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
    ILogger<CteLifecycleHooks> logger) : ICteAuthorizationHook, ICteCancellationHook
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
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "CT-e {AccessKey} autorizado, mas o título a receber não foi gerado.", doc.AccessKey);
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
        }

        await endorsementCancellation.CancelAsync(doc);
    }

    private static string User(TransportDocument doc) => doc.UpdatedBy ?? doc.CreatedBy ?? "Sistema";
}
