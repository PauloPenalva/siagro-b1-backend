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
/// Encerra o MDF-e autorizado (evento 110112). 135 vira Encerrado e libera os CT-e; 631 (evento já registrado) consulta
/// a chave e, se a SEFAZ diz encerrado (132), também marca Encerrado. Sem resposta da SEFAZ o MDF-e continua
/// Autorizado. O núcleo <see cref="CloseCoreAsync"/> é reutilizado pelo encerramento em lote.
/// </summary>
public class TransportManifestsCloseService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeNumberReservationService numbers,
    MdfeServiceContextFactory contexts,
    IMdfeSefazClient sefaz,
    Func<DateTimeOffset>? clock = null,
    ILogger<TransportManifestsCloseService>? logger = null)
{
    public const string NotAuthorized = "Só é possível encerrar MDF-e autorizado.";
    public const string FutureDate = "A data de encerramento não pode ser futura.";
    public const string NoResponse = "Sem resposta da SEFAZ: tente encerrar de novo.";
    public const string NoResponseAfterDuplicate =
        "A SEFAZ já tem um encerramento registrado, mas não respondeu à consulta de confirmação: consulte o MDF-e antes de tentar de novo.";
    public const string InvalidDate = "Data de encerramento inválida: use o formato AAAA-MM-DD.";

    /// <summary>Lê a data enviada como texto "yyyy-MM-dd" (vazio = padrão de hoje); texto inválido é recusado, nunca vira hoje.</summary>
    public static DateOnly? ParseClosedOn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : throw new DefaultException(InvalidDate);
    }

    /// <summary>cStat de evento já registrado (duplicidade de evento).</summary>
    public const int DuplicateEvent = 631;

    public async Task<NfeEventResult> CloseAsync(Guid key, DateOnly? closedOn, string? ufCode, string? cityIbge, string user)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        MdfeZeusConfiguration.PinGlobalLayout();

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await MdfeEmission.LoadAsync(db, key);
        if (m.Status != MdfeStatus.Authorized)
            throw new DefaultException(NotAuthorized);

        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstAsync(x => x.BranchCode == m.BranchCode);
        using var context = await contexts.OpenAsync(m.BranchCode, m.Environment ?? settings.Environment);

        return await CloseCoreAsync(m, context.Settings, closedOn, ufCode, cityIbge, user);
    }

    /// <summary>
    /// Núcleo reutilizável (o lote da entrega B chama uma vez por MDF-e, com o certificado já aberto e o bloqueio de
    /// emissão já tomado por quem chama). <paramref name="m"/> precisa vir de <c>MdfeEmission.LoadAsync</c> (CT-e e XMLs
    /// carregados). Padrões: hoje (Brasília), UF fim do MDF-e e último município de descarga. Lança
    /// <see cref="DefaultException"/> se não estiver Autorizado, se a data for futura ou se a SEFAZ não responder
    /// (nada muda). Retorna o resultado do evento. ATENÇÃO: 631 + consulta 132 devolve um resultado SINTÉTICO
    /// (135, "MDF-e já encerrado na SEFAZ.", sem protocolo, sem evento nem XML de encerramento gravados). 631 + consulta diferente
    /// de 132 devolve o próprio 631 e o MDF-e segue Autorizado. 631 + consulta sem resposta lança <see cref="NoResponseAfterDuplicate"/>.
    /// </summary>
    public async Task<NfeEventResult> CloseCoreAsync(
        TransportManifest m, MdfeServiceSettings settings, DateOnly? closedOn, string? ufCode, string? cityIbge, string user)
    {
        if (m.Status != MdfeStatus.Authorized || m.AccessKey is null || m.ProtocolNumber is null)
            throw new DefaultException(NotAuthorized);

        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);
        var date = closedOn ?? DateOnly.FromDateTime(now.DateTime);
        if (date > DateOnly.FromDateTime(now.DateTime))
            throw new DefaultException(FutureDate);

        var city = !string.IsNullOrWhiteSpace(cityIbge)
            ? cityIbge.Trim()
            : m.Ctes.OrderBy(c => c.LineNum).LastOrDefault()?.UnloadingCityIbge
              ?? throw new DefaultException("O MDF-e não tem município de descarga para o encerramento.");
        var uf = !string.IsNullOrWhiteSpace(ufCode)
            ? ufCode.Trim().ToUpperInvariant()
            : m.UfEnd ?? throw new DefaultException("Informe a UF do encerramento.");

        NfeEventResult result;
        try
        {
            result = await sefaz.CloseAsync(
                new MdfeCloseRequest(m.AccessKey, m.ProtocolNumber, m.AccessKey.Substring(6, 14), date, uf, city, now), settings);
        }
        catch (NfeCommunicationException e)
        {
            MdfeEmission.LogCommunication(logger, m.AccessKey, "encerramento", e);
            throw new DefaultException(NoResponse);
        }

        if (NfeStatusCodes.IsEventRegistered(result.StatusCode))
        {
            await MarkClosedAsync(m, result, date, uf, city, now, user);
            return result;
        }

        if (result.StatusCode == DuplicateEvent)
        {
            logger?.LogWarning("MDF-e {AccessKey}: SEFAZ respondeu {StatusCode} ({Reason}) ao encerrar; consultando a chave.",
                m.AccessKey, result.StatusCode, result.Reason);
            try
            {
                var consult = await sefaz.ConsultProtocolAsync(m.AccessKey, settings);
                if (consult.StatusCode == TransportManifestsConsultService.ClosedConsult)
                {
                    await MarkClosedAsync(m, null, date, uf, city, now, user);
                    return new NfeEventResult(135, "MDF-e já encerrado na SEFAZ.");
                }
            }
            catch (NfeCommunicationException e)
            {
                MdfeEmission.LogCommunication(logger, m.AccessKey, "consulta após duplicidade de evento", e);
                throw new DefaultException(NoResponseAfterDuplicate);
            }
        }

        return result;
    }

    private async Task MarkClosedAsync(
        TransportManifest m, NfeEventResult? result, DateOnly date, string uf, string city, DateTimeOffset now, string user)
    {
        m.ApplyStatus(MdfeStatus.Closed);
        m.ClosedOn = date;
        m.ClosingUf = uf;
        m.ClosingCityIbge = city;
        m.ClosedAt = result?.RegisteredAt?.DateTime ?? now.DateTime;
        m.UpdatedAt = DateTime.Now;
        m.UpdatedBy = user;

        if (result is not null)
        {
            db.Context.TransportManifestEvents.Add(new TransportManifestEvent
            {
                ManifestKey = m.Key, Type = MdfeEventType.Closing, Sequence = result.Sequence ?? 1,
                ProtocolNumber = result.Protocol, RegisteredAt = result.RegisteredAt?.DateTime ?? now.DateTime,
                StatusCode = result.StatusCode.ToString(),
                StatusReason = result.Reason.Length > 500 ? result.Reason[..500] : result.Reason,
                CreatedBy = user,
            });
            if (result.ProcEventXml is not null && m.Xmls.All(x => x.Kind != MdfeXmlKind.Closing))
                db.Context.TransportManifestXmls.Add(new TransportManifestXml
                {
                    ManifestKey = m.Key, Kind = MdfeXmlKind.Closing, Xml = result.ProcEventXml,
                });
        }

        await db.SaveChangesAsync();
    }
}
