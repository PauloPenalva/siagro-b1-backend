using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Consulta o protocolo do MDF-e pela chave. Recupera um MDF-e em Processamento/Rejeitado/Confirmado que a SEFAZ já
/// autorizou, sem reenviar, compondo o mdfeProc com o XML assinado gravado antes do envio. Um autorizado que a SEFAZ
/// diz encerrado (132) ou cancelado (101/151) passa a Encerrado/Cancelado. Fora disso a situação local não muda.
/// </summary>
public class TransportManifestsConsultService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeNumberReservationService numbers,
    MdfeServiceContextFactory contexts,
    IMdfeSefazClient sefaz,
    ILogger<TransportManifestsConsultService>? logger = null)
{
    /// <summary>cStat da consulta de MDF-e encerrado.</summary>
    public const int ClosedConsult = 132;

    public async Task<NfeSefazResult> ConsultAsync(Guid key)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        MdfeZeusConfiguration.PinGlobalLayout();

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await MdfeEmission.LoadAsync(db, key);
        if (m.AccessKey is null)
            throw new DefaultException("O MDF-e ainda não tem chave: confirme e transmita antes de consultar.");

        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstAsync(x => x.BranchCode == m.BranchCode);
        using var context = await contexts.OpenAsync(m.BranchCode, m.Environment ?? settings.Environment);

        NfeSefazResult result;
        try
        {
            result = await sefaz.ConsultProtocolAsync(m.AccessKey, context.Settings);
        }
        catch (NfeCommunicationException e)
        {
            MdfeEmission.LogCommunication(logger, m.AccessKey, "consulta", e);
            throw;
        }

        if (result.StatusCode == MdfeEmission.KeyDiffers && m.Status is MdfeStatus.Processing or MdfeStatus.Confirmed)
            return await MdfeEmission.ApplyAsync(
                db, m, MdfeEmission.BurnedNumber(logger, m.AccessKey, result), MdfeEmission.StoredSigned(m)?.Xml ?? "", logger);

        if (NfeStatusCodes.IsAuthorized(result.StatusCode)
            && m.Status is MdfeStatus.Processing or MdfeStatus.Rejected or MdfeStatus.Confirmed)
        {
            var stored = MdfeEmission.StoredSigned(m)
                         ?? throw new DefaultException("O MDF-e não tem o XML enviado gravado: não é possível compor o XML autorizado.");
            return await MdfeEmission.ApplyAsync(db, m, result, stored.Xml, logger);
        }

        if (m.Status == MdfeStatus.Authorized && result.StatusCode == ClosedConsult)
        {
            m.ApplyStatus(MdfeStatus.Closed);
            m.ClosedAt ??= DateTime.Now;
            m.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }
        else if (m.Status is MdfeStatus.Authorized or MdfeStatus.Closed && NfeStatusCodes.IsCancelledConsult(result.StatusCode))
        {
            m.ApplyStatus(MdfeStatus.Cancelled);
            m.CancelledAt ??= result.CancellationEvent?.RegisteredAt?.DateTime ?? DateTime.Now;
            m.UpdatedAt = DateTime.Now;
            if (result.CancellationEvent?.ProcEventXml is { } proc && m.Xmls.All(x => x.Kind != MdfeXmlKind.Cancellation))
                db.Context.TransportManifestXmls.Add(new TransportManifestXml { ManifestKey = m.Key, Kind = MdfeXmlKind.Cancellation, Xml = proc });
            await db.SaveChangesAsync();
        }
        else if (!NfeStatusCodes.IsAuthorized(result.StatusCode))
        {
            logger?.LogWarning("MDF-e {AccessKey}: consulta retornou {StatusCode} ({Reason}).", m.AccessKey, result.StatusCode, result.Reason);
        }

        return result;
    }
}
