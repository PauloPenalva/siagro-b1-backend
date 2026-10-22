using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Consulta o protocolo do CT-e pela chave. Recupera um CT-e em Processamento ou Rejeitado que a SEFAZ já autorizou,
/// sem reenviar, compondo o cteProc com o XML assinado gravado antes do envio — nunca um XML remontado.
/// Sem autorização na SEFAZ, a situação local não muda.
/// </summary>
public class TransportDocumentsConsultService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    CteServiceContextFactory contexts,
    ICteSefazClient sefaz,
    ILogger<TransportDocumentsConsultService>? logger = null)
{
    public async Task<NfeSefazResult> ConsultAsync(Guid key)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await CteEmission.LoadAsync(db, key);
        if (doc.AccessKey is null)
            throw new DefaultException("O CT-e ainda não tem chave: confirme e transmita antes de consultar.");

        var settings = await db.Context.BranchCteSettings.AsNoTracking().FirstAsync(x => x.BranchCode == doc.BranchCode);
        using var context = await contexts.OpenAsync(doc.BranchCode, doc.Environment ?? settings.Environment);

        var result = await sefaz.ConsultProtocolAsync(doc.AccessKey, context.Settings);

        if (NfeStatusCodes.IsAuthorized(result.StatusCode)
            && doc.Status is CteStatus.Processing or CteStatus.Rejected or CteStatus.Confirmed)
        {
            var stored = CteEmission.StoredSigned(doc)
                         ?? throw new DefaultException("O CT-e não tem o XML enviado gravado: não é possível compor o XML autorizado.");
            return await CteEmission.ApplyAsync(db, doc, result, stored.Xml, logger);
        }

        return result;
    }
}
