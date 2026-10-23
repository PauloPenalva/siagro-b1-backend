using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Cancela na AT&amp;M a averbação de um CT-e já cancelado na SEFAZ, enviando o procEventoCTe ao mesmo /rest/CTE.
/// Falha não desfaz o cancelamento: a averbação fica "Cancelamento pendente" e pode ser reenviada.
/// </summary>
public class CteEndorsementCancellationService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    IAverbaClient averba,
    ILogger<CteEndorsementCancellationService> logger)
{
    public const string NotPending = "Só é possível reenviar o cancelamento da averbação de um CT-e cancelado com o cancelamento pendente.";

    /// <summary>Nunca lança: o cancelamento na SEFAZ já ocorreu. Só age em averbação Endorsed ou CancellationPending.</summary>
    public async Task CancelAsync(TransportDocument doc)
    {
        try
        {
            if (doc.EndorsementStatus is not (CteEndorsementStatus.Endorsed or CteEndorsementStatus.CancellationPending))
                return;

            var error = await SendAsync(doc);
            if (error is not null)
                await MarkPendingAsync(doc, error);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Falha ao cancelar a averbação do CT-e {AccessKey}", doc.AccessKey);
            try
            {
                await MarkPendingAsync(doc, e.Message);
            }
            catch (Exception inner)
            {
                logger.LogWarning(inner, "Não foi possível gravar a pendência do cancelamento da averbação do CT-e {AccessKey}", doc.AccessKey);
            }
        }
    }

    /// <summary>Reenvio manual: exige CT-e cancelado e averbação com cancelamento pendente.</summary>
    public async Task<AverbaTestResult> ResendAsync(Guid key, string user, CancellationToken ct = default)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await db.Context.TransportDocuments.FirstOrDefaultAsync(x => x.Key == key, ct)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Cancelled || doc.EndorsementStatus != CteEndorsementStatus.CancellationPending)
            return new AverbaTestResult { Success = false, Message = NotPending };

        doc.UpdatedBy = user;
        await CancelAsync(doc);

        return doc.EndorsementStatus == CteEndorsementStatus.Cancelled
            ? new AverbaTestResult { Success = true, Message = $"Averbação cancelada. Protocolo {doc.EndorsementCancellationProtocol}." }
            : new AverbaTestResult { Success = false, Message = doc.EndorsementError ?? "A AT&M recusou o cancelamento." };
    }

    /// <summary>Devolve null no sucesso (já gravado) ou o motivo da falha.</summary>
    private async Task<string?> SendAsync(TransportDocument doc)
    {
        var settings = await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == doc.BranchCode);
        if (!AverbaLogin.IsConfigured(settings))
            return TransportDocumentsEndorseService.NotConfigured;

        var xml = await db.Context.TransportDocumentXmls.AsNoTracking()
            .Where(x => x.TransportDocumentKey == doc.Key && x.Kind == CteXmlKind.Cancellation)
            .Select(x => x.Xml).FirstOrDefaultAsync();
        if (xml is null)
            return "O XML do evento de cancelamento do CT-e não foi encontrado.";

        var login = await AverbaLogin.LoginAsync(averba, configuration, settings!, CancellationToken.None);
        if (!login.Success || string.IsNullOrWhiteSpace(login.Bearer))
            return login.Error ?? "Falha no login da AT&M.";

        var sent = await averba.SendCteAsync(settings!.AverbaEnvironment, login.Bearer, xml, CancellationToken.None);
        if (!sent.Success)
            return sent.Error ?? "A AT&M recusou o cancelamento da averbação.";

        doc.EndorsementStatus = CteEndorsementStatus.Cancelled;
        doc.EndorsementCancellationProtocol = sent.Protocol is { Length: > 40 } p ? p[..40] : sent.Protocol;
        doc.EndorsementError = null;
        doc.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return null;
    }

    private async Task MarkPendingAsync(TransportDocument doc, string error)
    {
        logger.LogWarning("Cancelamento da averbação do CT-e {AccessKey} pendente: {Error}", doc.AccessKey, error);
        doc.EndorsementStatus = CteEndorsementStatus.CancellationPending;
        doc.EndorsementError = error.Length > 500 ? error[..500] : error;
        doc.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}
