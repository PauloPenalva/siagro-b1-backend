using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Inclui um condutor no MDF-e autorizado (evento 110114). 135 grava o condutor (AddedByEvent), o evento e o XML do
/// evento. A sequência do evento é por tipo: último evento de inclusão de condutor + 1. Sem resposta da SEFAZ nada muda.
/// </summary>
public class TransportManifestsIncludeDriverService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeNumberReservationService numbers,
    MdfeServiceContextFactory contexts,
    IMdfeSefazClient sefaz,
    Func<DateTimeOffset>? clock = null,
    ILogger<TransportManifestsIncludeDriverService>? logger = null)
{
    public const string NotAuthorized = "Só é possível incluir condutor em MDF-e autorizado.";
    public const string InvalidCpf = "CPF do condutor inválido.";
    public const string NameRequired = "Informe o nome do condutor.";
    public const string AlreadyIncluded = "O condutor já está no MDF-e.";
    public const string NoResponse = "Sem resposta da SEFAZ: tente incluir o condutor de novo.";

    public async Task<NfeEventResult> IncludeDriverAsync(Guid key, string? cpf, string? name, string user)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        MdfeZeusConfiguration.PinGlobalLayout();

        var digits = new string((cpf ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 11 || !TaxIdValidator.IsValid(digits))
            throw new DefaultException(InvalidCpf);
        var driverName = (name ?? string.Empty).Trim();
        if (driverName.Length == 0)
            throw new DefaultException(NameRequired);
        if (driverName.Length > 60) driverName = driverName[..60];

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await MdfeEmission.LoadAsync(db, key);
        if (m.Status != MdfeStatus.Authorized || m.AccessKey is null)
            throw new DefaultException(NotAuthorized);

        var mainCpf = new string((m.DriverCpf ?? string.Empty).Where(char.IsDigit).ToArray());
        if (mainCpf == digits || m.Drivers.Any(d => d.Cpf == digits))
            throw new DefaultException(AlreadyIncluded);

        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstAsync(x => x.BranchCode == m.BranchCode);
        using var context = await contexts.OpenAsync(m.BranchCode, m.Environment ?? settings.Environment);

        var last = await db.Context.TransportManifestEvents
            .Where(x => x.ManifestKey == m.Key && x.Type == MdfeEventType.DriverInclusion)
            .MaxAsync(x => (int?)x.Sequence) ?? 0;
        var sequence = last + 1;
        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);

        NfeEventResult result;
        try
        {
            result = await sefaz.IncludeDriverAsync(
                new MdfeIncludeDriverRequest(m.AccessKey, m.AccessKey.Substring(6, 14), sequence, digits, driverName, now),
                context.Settings);
        }
        catch (NfeCommunicationException e)
        {
            MdfeEmission.LogCommunication(logger, m.AccessKey, "inclusão de condutor", e);
            throw new DefaultException(NoResponse);
        }

        if (!NfeStatusCodes.IsEventRegistered(result.StatusCode))
            return result;

        var registeredAt = result.RegisteredAt?.DateTime ?? now.DateTime;
        db.Context.TransportManifestDrivers.Add(new TransportManifestDriver
        {
            ManifestKey = m.Key, LineNum = m.Drivers.Select(d => d.LineNum).DefaultIfEmpty(0).Max() + 1,
            Cpf = digits, Name = driverName, AddedByEvent = true, EventSequence = result.Sequence ?? sequence,
        });
        db.Context.TransportManifestEvents.Add(new TransportManifestEvent
        {
            ManifestKey = m.Key, Type = MdfeEventType.DriverInclusion, Sequence = result.Sequence ?? sequence,
            ProtocolNumber = result.Protocol, RegisteredAt = registeredAt,
            StatusCode = result.StatusCode.ToString(),
            StatusReason = result.Reason.Length > 500 ? result.Reason[..500] : result.Reason,
            CreatedBy = user,
        });
        if (result.ProcEventXml is not null)
            db.Context.TransportManifestXmls.Add(new TransportManifestXml
            {
                ManifestKey = m.Key, Kind = MdfeXmlKind.DriverInclusion, Xml = result.ProcEventXml,
            });
        m.UpdatedAt = DateTime.Now;
        m.UpdatedBy = user;
        await db.SaveChangesAsync();
        return result;
    }
}
