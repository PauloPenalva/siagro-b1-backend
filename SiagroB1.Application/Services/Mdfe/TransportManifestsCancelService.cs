using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Cancela o MDF-e autorizado (evento 110111) em até 24 horas da autorização. 135 vira Cancelado e libera os CT-e.
/// Se a SEFAZ recusar o evento, consulta a chave: se o MDF-e já está encerrado (132) marca Encerrado (o cancelamento
/// não vale mais), e se já está cancelado (101/151) marca Cancelado. Sem resposta da SEFAZ o MDF-e continua Autorizado.
/// </summary>
public class TransportManifestsCancelService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeNumberReservationService numbers,
    MdfeServiceContextFactory contexts,
    IMdfeSefazClient sefaz,
    Func<DateTimeOffset>? clock = null,
    ILogger<TransportManifestsCancelService>? logger = null)
{
    public const string NotAuthorized = "Só é possível cancelar MDF-e autorizado e não encerrado.";
    public const string InvalidJustification = "A justificativa do cancelamento tem de 15 a 255 caracteres.";
    public const string DeadlinePassed = "O prazo de cancelamento do MDF-e (24 horas) acabou.";
    public const string AlreadyClosed = "O MDF-e já está encerrado na SEFAZ; a situação foi atualizada.";
    public const string AlreadyCancelled = "O MDF-e já está cancelado na SEFAZ; a situação foi atualizada.";
    public const string NoResponse = "Sem resposta da SEFAZ: tente cancelar de novo.";

    public async Task<NfeEventResult> CancelAsync(Guid key, string justification, string user)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        MdfeZeusConfiguration.PinGlobalLayout();

        var text = (justification ?? string.Empty).Trim();
        if (text.Length is < 15 or > 255)
            throw new DefaultException(InvalidJustification);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await MdfeEmission.LoadAsync(db, key);
        if (m.Status != MdfeStatus.Authorized || m.AccessKey is null || m.ProtocolNumber is null)
            throw new DefaultException(NotAuthorized);

        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);
        if (m.AuthorizedAt is { } authorizedAt && now.DateTime > authorizedAt.AddHours(24))
            throw new DefaultException(DeadlinePassed);

        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstAsync(x => x.BranchCode == m.BranchCode);
        using var context = await contexts.OpenAsync(m.BranchCode, m.Environment ?? settings.Environment);

        NfeEventResult result;
        try
        {
            result = await sefaz.CancelAsync(
                new MdfeCancelRequest(m.AccessKey, m.ProtocolNumber, text, m.AccessKey.Substring(6, 14), now), context.Settings);
        }
        catch (NfeCommunicationException e)
        {
            MdfeEmission.LogCommunication(logger, m.AccessKey, "cancelamento", e);
            throw new DefaultException(NoResponse);
        }

        if (NfeStatusCodes.IsEventRegistered(result.StatusCode))
        {
            m.ApplyStatus(MdfeStatus.Cancelled);
            m.CancelledAt = result.RegisteredAt?.DateTime ?? now.DateTime;
            m.UpdatedAt = DateTime.Now;
            m.UpdatedBy = user;
            db.Context.TransportManifestEvents.Add(new TransportManifestEvent
            {
                ManifestKey = m.Key, Type = MdfeEventType.Cancellation, Sequence = result.Sequence ?? 1,
                ProtocolNumber = result.Protocol, RegisteredAt = result.RegisteredAt?.DateTime ?? now.DateTime,
                StatusCode = result.StatusCode.ToString(),
                StatusReason = result.Reason.Length > 500 ? result.Reason[..500] : result.Reason,
                Justification = text, CreatedBy = user,
            });
            if (result.ProcEventXml is not null && m.Xmls.All(x => x.Kind != MdfeXmlKind.Cancellation))
                db.Context.TransportManifestXmls.Add(new TransportManifestXml
                {
                    ManifestKey = m.Key, Kind = MdfeXmlKind.Cancellation, Xml = result.ProcEventXml,
                });
            await db.SaveChangesAsync();
            return result;
        }

        // Recusado: o MDF-e pode já estar encerrado (ou cancelado) na SEFAZ; a consulta decide a situação local.
        logger?.LogWarning("MDF-e {AccessKey}: SEFAZ respondeu {StatusCode} ({Reason}) ao cancelar; consultando a chave.",
            m.AccessKey, result.StatusCode, result.Reason);
        try
        {
            var consult = await sefaz.ConsultProtocolAsync(m.AccessKey, context.Settings);
            if (consult.StatusCode == TransportManifestsConsultService.ClosedConsult)
            {
                m.ApplyStatus(MdfeStatus.Closed);
                m.ClosedAt ??= now.DateTime;
                m.ClosedOn ??= DateOnly.FromDateTime(now.DateTime);
                m.UpdatedAt = DateTime.Now;
                m.UpdatedBy = user;
                await db.SaveChangesAsync();
                return new NfeEventResult(consult.StatusCode, AlreadyClosed);
            }
            else if (NfeStatusCodes.IsCancelledConsult(consult.StatusCode))
            {
                m.ApplyStatus(MdfeStatus.Cancelled);
                m.CancelledAt ??= now.DateTime;
                m.UpdatedAt = DateTime.Now;
                m.UpdatedBy = user;
                await db.SaveChangesAsync();
                return new NfeEventResult(consult.StatusCode, AlreadyCancelled);
            }
        }
        catch (NfeCommunicationException e)
        {
            MdfeEmission.LogCommunication(logger, m.AccessKey, "consulta após recusa do cancelamento", e);
        }

        return result;
    }
}
