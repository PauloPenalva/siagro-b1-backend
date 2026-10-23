using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Transmite o MDF-e com o número e a chave fixados na confirmação (espelho do CT-e). O XML assinado é gravado (Kind
/// Signed) junto com o Processamento, ANTES do envio; o mdfeProc só nasce dele. Sem resposta da SEFAZ o MDF-e fica em
/// Processamento; a próxima transmissão consulta pela chave primeiro e, só se a SEFAZ não o conhece, reenvia o MESMO
/// XML (mesma dhEmi). Só a primeira transmissão ou uma rejeição processada pela SEFAZ renovam a dhEmi.
/// </summary>
public class TransportManifestsTransmitService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeNumberReservationService numbers,
    MdfeServiceContextFactory contexts,
    IMdfeSefazClient sefaz,
    NfeOptions options,
    Func<DateTimeOffset>? clock = null,
    ILogger<TransportManifestsTransmitService>? logger = null)
{
    public const string NotTransmittable = "Só é possível transmitir MDF-e confirmado, rejeitado ou em processamento.";
    public const string NoResponse = MdfeEmission.NoResponse;
    public const string BurnedNumberReason = MdfeEmission.BurnedNumberReason;

    public async Task<NfeSefazResult> TransmitAsync(Guid key, string user)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        MdfeZeusConfiguration.PinGlobalLayout();

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await MdfeEmission.LoadAsync(db, key);
        if (m.Status is not (MdfeStatus.Confirmed or MdfeStatus.Rejected or MdfeStatus.Processing))
            throw new DefaultException(NotTransmittable);
        if (m.AccessKey is null)
            throw new DefaultException("Confirme o MDF-e antes de transmitir: ele ainda não tem número e chave.");

        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstAsync(x => x.BranchCode == m.BranchCode);
        using var context = await contexts.OpenAsync(m.BranchCode, m.Environment ?? settings.Environment);

        var stored = MdfeEmission.StoredSigned(m);
        if (m.Status == MdfeStatus.Processing && stored is not null)
        {
            // Situação desconhecida: consulta pela chave antes de qualquer reenvio.
            NfeSefazResult consult;
            try
            {
                consult = await sefaz.ConsultProtocolAsync(m.AccessKey, context.Settings);
            }
            catch (NfeCommunicationException e)
            {
                MdfeEmission.LogCommunication(logger, m.AccessKey, "consulta", e);
                return await MdfeEmission.ApplyAsync(db, m, null, stored.Xml, logger);
            }

            if (NfeStatusCodes.IsAuthorized(consult.StatusCode))
                return await MdfeEmission.ApplyAsync(db, m, consult, stored.Xml, logger);
            if (consult.StatusCode == MdfeEmission.KeyDiffers)
                return await MdfeEmission.ApplyAsync(db, m, MdfeEmission.BurnedNumber(logger, m.AccessKey, consult), stored.Xml, logger);

            return await SendAsync(m, new SignedMdfe(stored.Xml, m.AccessKey), context.Settings);
        }

        // Primeira transmissão ou rejeição processada pela SEFAZ: renova a dhEmi e assina de novo.
        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);
        var keyMonth = m.AccessKey.Substring(2, 4);
        if (now.ToString("yyMM") != keyMonth)
            throw new DefaultException(
                $"A chave foi gerada em {keyMonth[2..]}/20{keyMonth[..2]} e a emissão cairia em outro mês. Descarte o número e confirme de novo.");

        m.IssuedAt = now.DateTime;
        var signed = await SignAsync(m, context.Settings);

        // O XML exato vai para o banco no mesmo SaveChanges do Processamento, antes do envio.
        if (stored is not null)
            db.Context.TransportManifestXmls.Remove(stored);
        db.Context.TransportManifestXmls.Add(new TransportManifestXml { ManifestKey = m.Key, Kind = MdfeXmlKind.Signed, Xml = signed.Xml });
        m.ApplyStatus(MdfeStatus.Processing);
        m.UpdatedAt = DateTime.Now;
        m.UpdatedBy = user;
        await db.SaveChangesAsync();

        return await SendAsync(m, signed, context.Settings);
    }

    private async Task<SignedMdfe> SignAsync(TransportManifest m, MdfeServiceSettings serviceSettings)
    {
        var branch = await db.Context.Branchs.AsNoTracking().Include(b => b.Municipality).FirstOrDefaultAsync(b => b.Code == m.BranchCode)
                     ?? throw new DefaultException($"Filial {m.BranchCode} não encontrada.");
        var transport = await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == m.BranchCode)
                        ?? throw new DefaultException(MdfeReadinessValidator.RntrcRequired);
        var input = MdfeIssueInputAssembler.Build(m, branch, transport, options.TechnicalResponsible);

        MdfeZeusConfiguration.PinGlobalLayout();
        SignedMdfe signed;
        try
        {
            signed = MdfeSigner.SignAndValidate(MdfeXmlBuilder.Build(input), serviceSettings);
        }
        catch (NfeValidationException e)
        {
            throw new DefaultException($"O XML do MDF-e não passou na validação: {e.Message}");
        }

        if (signed.AccessKey != m.AccessKey)
            throw new DefaultException(
                $"A chave montada ({signed.AccessKey}) difere da gravada na confirmação ({m.AccessKey}). Descarte o número e confirme de novo.");
        return signed;
    }

    private async Task<NfeSefazResult> SendAsync(TransportManifest m, SignedMdfe signed, MdfeServiceSettings settings)
    {
        NfeSefazResult? result;
        try
        {
            result = await sefaz.AuthorizeAsync(signed, settings);
            if (MdfeEmission.IsDuplicate(result.StatusCode))
            {
                logger?.LogWarning("MDF-e {AccessKey}: SEFAZ respondeu duplicidade {StatusCode} ({Reason}); consultando a chave.",
                    signed.AccessKey, result.StatusCode, result.Reason);
                result = await RecoverAsync(signed.AccessKey, settings, result);
            }
        }
        catch (NfeCommunicationException e)
        {
            MdfeEmission.LogCommunication(logger, signed.AccessKey, "autorização", e);
            result = await RecoverAsync(signed.AccessKey, settings, null);
        }

        return await MdfeEmission.ApplyAsync(db, m, result, signed.Xml, logger);
    }

    /// <summary>
    /// Só a consulta pela chave diz se a SEFAZ autorizou. Nulo = sem resposta (fica em Processamento). 216, ou não
    /// encontrado depois de 539 (duplicidade com outra chave) = número queimado por outro MDF-e: rejeita para descartar.
    /// </summary>
    private async Task<NfeSefazResult?> RecoverAsync(string accessKey, MdfeServiceSettings settings, NfeSefazResult? duplicate)
    {
        try
        {
            var consult = await sefaz.ConsultProtocolAsync(accessKey, settings);
            if (NfeStatusCodes.IsAuthorized(consult.StatusCode))
                return consult;
            if (consult.StatusCode == MdfeEmission.KeyDiffers)
                return MdfeEmission.BurnedNumber(logger, accessKey, consult);
            if (duplicate?.StatusCode == NfeStatusCodes.NumberUsedByAnotherKey && consult.StatusCode == MdfeEmission.NotFound)
                return MdfeEmission.BurnedNumber(logger, accessKey, duplicate);
            logger?.LogWarning("MDF-e {AccessKey}: consulta de recuperação retornou {StatusCode} ({Reason}); fica em Processamento.",
                accessKey, consult.StatusCode, consult.Reason);
        }
        catch (NfeCommunicationException e)
        {
            MdfeEmission.LogCommunication(logger, accessKey, "consulta", e);
        }

        return null;
    }
}

/// <summary>Partes comuns da transmissão e da consulta do MDF-e.</summary>
internal static class MdfeEmission
{
    public const string NoResponse = "Sem resposta da SEFAZ: consulte depois.";
    public const string BurnedNumberReason =
        "Número já usado na SEFAZ por outro MDF-e (chave diferente). Descarte o número e confirme de novo.";
    public const string DigestMismatch = "Protocolo da SEFAZ não confere com o XML enviado: verifique.";
    public const int KeyDiffers = 216;
    public const int NotFound = 217;

    /// <summary>Duplicidade (204/539 como na NF-e, mais 609 do MDF-e): só a consulta resolve.</summary>
    public static bool IsDuplicate(int code) => NfeStatusCodes.IsDuplicate(code) || code == 609;

    public static async Task<TransportManifest> LoadAsync(IUnitOfWork db, Guid key) =>
        await db.Context.TransportManifests
            .Include(x => x.Ctes).Include(x => x.LoadingCities).Include(x => x.RouteUfs).Include(x => x.Vehicles)
            .Include(x => x.Drivers).Include(x => x.Contractors).Include(x => x.Seals).Include(x => x.Xmls)
            .Include(x => x.Payments).ThenInclude(p => p.Components)
            .Include(x => x.Payments).ThenInclude(p => p.Installments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Key == key)
        ?? throw new DefaultException(TransportManifestsSaveService.NotFound);

    public static TransportManifestXml? StoredSigned(TransportManifest m) => m.Xmls.FirstOrDefault(x => x.Kind == MdfeXmlKind.Signed);

    public static NfeSefazResult BurnedNumber(ILogger? logger, string accessKey, NfeSefazResult sefazAnswer)
    {
        logger?.LogWarning("MDF-e {AccessKey}: número queimado na SEFAZ por outro MDF-e — {StatusCode} ({Reason}).",
            accessKey, sefazAnswer.StatusCode, sefazAnswer.Reason);
        return new NfeSefazResult(sefazAnswer.StatusCode, BurnedNumberReason);
    }

    public static void LogCommunication(ILogger? logger, string accessKey, string operation, NfeCommunicationException e) =>
        logger?.LogWarning(e, "MDF-e {AccessKey}: falha de comunicação com a SEFAZ na {Operation}, sem código de status: {Message}",
            accessKey, operation, e.Message);

    /// <summary>
    /// Grava o resultado. Nulo = sem resposta: fica em Processamento. O mdfeProc só nasce do XML assinado gravado e só se
    /// o digVal do protMDFe bater com o DigestValue dele. Denegado libera os CT-e (via ApplyStatus).
    /// </summary>
    public static async Task<NfeSefazResult> ApplyAsync(
        IUnitOfWork db, TransportManifest m, NfeSefazResult? result, string signedXml, ILogger? logger)
    {
        m.UpdatedAt = DateTime.Now;

        if (result is null)
        {
            m.ApplyStatus(MdfeStatus.Processing);
            m.RejectionCode = null;
            m.RejectionReason = NoResponse;
            await db.SaveChangesAsync();
            return new NfeSefazResult(0, NoResponse);
        }

        if (NfeStatusCodes.IsAuthorized(result.StatusCode))
        {
            var signedDigest = NfeProcComposer.SignedDigest(signedXml);
            var protocolDigest = result.ProtocolXml is null ? null : NfeProcComposer.ProtocolDigest(result.ProtocolXml);
            if (signedDigest is null || signedDigest != protocolDigest)
            {
                logger?.LogError("MDF-e {AccessKey}: digVal do protocolo ({ProtocolDigest}) difere do XML enviado ({SignedDigest}).",
                    m.AccessKey, protocolDigest, signedDigest);
                m.ApplyStatus(MdfeStatus.Processing);
                m.RejectionCode = null;
                m.RejectionReason = DigestMismatch;
                await db.SaveChangesAsync();
                return new NfeSefazResult(0, DigestMismatch);
            }

            m.ApplyStatus(MdfeStatus.Authorized);
            m.ProtocolNumber = result.Protocol;
            m.AuthorizedAt = result.ReceivedAt?.DateTime;
            m.RejectionCode = null;
            m.RejectionReason = null;
            if (m.Xmls.All(x => x.Kind != MdfeXmlKind.Authorized))
                db.Context.TransportManifestXmls.Add(new TransportManifestXml
                {
                    ManifestKey = m.Key, Kind = MdfeXmlKind.Authorized, Xml = MdfeProc.Compose(signedXml, result.ProtocolXml!),
                });
        }
        else
        {
            m.ApplyStatus(NfeStatusCodes.IsDenied(result.StatusCode) ? MdfeStatus.Denied : MdfeStatus.Rejected);
            m.RejectionCode = result.StatusCode.ToString();
            m.RejectionReason = result.Reason.Length > 500 ? result.Reason[..500] : result.Reason;
        }

        await db.SaveChangesAsync();
        return result;
    }
}
