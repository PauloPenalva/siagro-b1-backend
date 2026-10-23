using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Transmite o CT-e com o número e a chave fixados na confirmação. O XML assinado é gravado (Kind Signed) junto com o
/// Processamento, ANTES do envio; o cteProc só nasce dele. Sem resposta da SEFAZ o CT-e fica em Processamento; a próxima
/// transmissão consulta primeiro e, se a SEFAZ não o conhece, reenvia o MESMO XML (mesma dhEmi). Só a primeira
/// transmissão ou uma rejeição processada pela SEFAZ renovam a dhEmi e assinam de novo.
/// </summary>
public class TransportDocumentsTransmitService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    CteServiceContextFactory contexts,
    ICteSefazClient sefaz,
    NfeOptions options,
    Func<DateTimeOffset>? clock = null,
    ILogger<TransportDocumentsTransmitService>? logger = null,
    ICteAuthorizationHook? authorizationHook = null)
{
    public const string NotTransmittable = "Só é possível transmitir CT-e confirmado, rejeitado ou em processamento.";
    public const string NoResponse = CteEmission.NoResponse;

    public async Task<NfeSefazResult> TransmitAsync(Guid key, string user)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await CteEmission.LoadAsync(db, key);
        if (doc.Status is not (CteStatus.Confirmed or CteStatus.Rejected or CteStatus.Processing))
            throw new DefaultException(NotTransmittable);
        if (doc.AccessKey is null)
            throw new DefaultException("Confirme o CT-e antes de transmitir: ele ainda não tem número e chave.");

        var settings = await db.Context.BranchCteSettings.AsNoTracking().FirstAsync(x => x.BranchCode == doc.BranchCode);
        using var context = await contexts.OpenAsync(doc.BranchCode, doc.Environment ?? settings.Environment);

        var stored = CteEmission.StoredSigned(doc);
        if (doc.Status == CteStatus.Processing && stored is not null)
        {
            // Situação desconhecida: consulta antes de qualquer reenvio.
            NfeSefazResult? consult;
            try
            {
                consult = await sefaz.ConsultProtocolAsync(doc.AccessKey, context.Settings);
            }
            catch (NfeCommunicationException e)
            {
                CteEmission.LogCommunication(logger, doc.AccessKey, "consulta", e);
                return await CteEmission.ApplyAsync(db, doc, null, stored.Xml, logger, authorizationHook);
            }

            if (NfeStatusCodes.IsAuthorized(consult.StatusCode))
                return await CteEmission.ApplyAsync(db, doc, consult, stored.Xml, logger, authorizationHook);
            if (consult.StatusCode == CteEmission.KeyDiffers)
                return await CteEmission.ApplyAsync(db, doc, CteEmission.BurnedNumber(logger, doc.AccessKey, consult), stored.Xml, logger, authorizationHook);

            return await SendAsync(doc, new SignedCte(stored.Xml, doc.AccessKey), context.Settings);
        }

        // Primeira transmissão ou rejeição processada pela SEFAZ: renova a dhEmi e assina de novo.
        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);
        var keyMonth = doc.AccessKey.Substring(2, 4);
        if (now.ToString("yyMM") != keyMonth)
            throw new DefaultException(
                $"A chave foi gerada em {keyMonth[2..]}/20{keyMonth[..2]} e a emissão cairia em outro mês. Descarte o número e confirme de novo.");

        doc.IssuedAt = now.DateTime;
        var signed = await CteEmission.SignAsync(db, doc, settings, context.Settings, options);

        // O XML exato vai para o banco no mesmo SaveChanges do Processamento, antes do envio.
        if (stored is not null)
            db.Context.TransportDocumentXmls.Remove(stored);
        db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
        {
            TransportDocumentKey = doc.Key, Kind = CteXmlKind.Signed, Xml = signed.Xml,
        });
        doc.Status = CteStatus.Processing;
        doc.UpdatedAt = DateTime.Now;
        doc.UpdatedBy = user;
        await db.SaveChangesAsync();

        return await SendAsync(doc, signed, context.Settings);
    }

    private async Task<NfeSefazResult> SendAsync(TransportDocument doc, SignedCte signed, CteServiceSettings settings)
    {
        NfeSefazResult? result;
        try
        {
            result = await sefaz.AuthorizeAsync(signed, settings);
            if (NfeStatusCodes.IsDuplicate(result.StatusCode))
            {
                logger?.LogWarning("CT-e {AccessKey}: SEFAZ respondeu duplicidade {StatusCode} ({Reason}); consultando a chave.",
                    signed.AccessKey, result.StatusCode, result.Reason);
                result = await RecoverAsync(signed.AccessKey, settings, result);
            }
        }
        catch (NfeCommunicationException e)
        {
            CteEmission.LogCommunication(logger, signed.AccessKey, "autorização", e);
            result = await RecoverAsync(signed.AccessKey, settings, null);
        }

        return await CteEmission.ApplyAsync(db, doc, result, signed.Xml, logger, authorizationHook);
    }

    /// <summary>
    /// Só a consulta pela chave diz se a SEFAZ autorizou. Nulo = sem resposta (fica em Processamento).
    /// 216 (chave difere da cadastrada), ou 217 depois de 539 (outra chave), = o número foi queimado por outro CT-e na
    /// SEFAZ: rejeita para o usuário descartar o número — reenviar daria duplicidade para sempre.
    /// 204 (esta mesma chave) com 217 = atraso da SEFAZ: fica em Processamento para a consulta posterior.
    /// </summary>
    private async Task<NfeSefazResult?> RecoverAsync(string accessKey, CteServiceSettings settings, NfeSefazResult? duplicate)
    {
        try
        {
            var consult = await sefaz.ConsultProtocolAsync(accessKey, settings);
            if (NfeStatusCodes.IsAuthorized(consult.StatusCode))
                return consult;
            if (consult.StatusCode == CteEmission.KeyDiffers)
                return CteEmission.BurnedNumber(logger, accessKey, consult);
            if (duplicate?.StatusCode == NfeStatusCodes.NumberUsedByAnotherKey && consult.StatusCode == CteEmission.NotFound)
                return CteEmission.BurnedNumber(logger, accessKey, duplicate);
            logger?.LogWarning("CT-e {AccessKey}: consulta de recuperação retornou {StatusCode} ({Reason}); fica em Processamento.",
                accessKey, consult.StatusCode, consult.Reason);
        }
        catch (NfeCommunicationException e)
        {
            CteEmission.LogCommunication(logger, accessKey, "consulta", e);
        }

        return null;
    }
}

/// <summary>Partes comuns da transmissão e da consulta do CT-e.</summary>
internal static class CteEmission
{
    private const string CteNamespace = "http://www.portalfiscal.inf.br/cte";

    public const string NoResponse = "Sem resposta da SEFAZ: consulte depois.";
    public const string BurnedNumberReason =
        "Número já usado na SEFAZ por outro CT-e (chave diferente). Descarte o número e confirme de novo.";
    public const int KeyDiffers = 216;
    public const int NotFound = 217;

    public static NfeSefazResult BurnedNumber(ILogger? logger, string accessKey, NfeSefazResult sefazAnswer)
    {
        logger?.LogWarning("CT-e {AccessKey}: número queimado na SEFAZ por outro CT-e — {StatusCode} ({Reason}).",
            accessKey, sefazAnswer.StatusCode, sefazAnswer.Reason);
        return new NfeSefazResult(sefazAnswer.StatusCode, BurnedNumberReason);
    }

    public static void LogCommunication(ILogger? logger, string accessKey, string operation, NfeCommunicationException e) =>
        logger?.LogWarning(e, "CT-e {AccessKey}: falha de comunicação com a SEFAZ na {Operation}, sem código de status: {Message}",
            accessKey, operation, e.Message);

    public const string DigestMismatch = "Protocolo da SEFAZ não confere com o XML enviado: verifique.";

    public static async Task<TransportDocument> LoadAsync(IUnitOfWork db, Guid key) =>
        await db.Context.TransportDocuments
            .Include(x => x.Parties).Include(x => x.NfeKeys).Include(x => x.Quantities).Include(x => x.Components)
            .Include(x => x.Xmls)
            .FirstOrDefaultAsync(x => x.Key == key)
        ?? throw new DefaultException("CT-e não encontrado.");

    public static TransportDocumentXml? StoredSigned(TransportDocument doc) =>
        doc.Xmls.FirstOrDefault(x => x.Kind == CteXmlKind.Signed);

    public static async Task<SignedCte> SignAsync(
        IUnitOfWork db, TransportDocument doc, BranchCteSettings settings, CteServiceSettings serviceSettings, NfeOptions options)
    {
        var branch = await db.Context.Branchs.AsNoTracking().Include(b => b.Municipality).FirstOrDefaultAsync(b => b.Code == doc.BranchCode)
                     ?? throw new DefaultException($"Filial {doc.BranchCode} não encontrada.");
        var names = await CteIssueInputAssembler.MunicipalityNamesAsync(db, doc);
        var input = CteIssueInputAssembler.Build(doc, branch, settings, names, options.TechnicalResponsible);

        CteZeusConfiguration.PinGlobalLayout();
        SignedCte signed;
        try
        {
            signed = CteSigner.SignAndValidate(CteXmlBuilder.Build(input), serviceSettings);
        }
        catch (NfeValidationException e)
        {
            throw new DefaultException($"O XML do CT-e não passou na validação: {e.Message}");
        }

        if (signed.AccessKey != doc.AccessKey)
            throw new DefaultException(
                $"A chave montada ({signed.AccessKey}) difere da gravada na confirmação ({doc.AccessKey}). Descarte o número e confirme de novo.");

        return signed;
    }

    /// <summary>
    /// Grava o resultado. <paramref name="result"/> nulo = sem resposta: fica em Processamento. O cteProc só nasce do
    /// XML assinado gravado e só se o digVal do protCTe bater com o DigestValue dele.
    /// </summary>
    public static async Task<NfeSefazResult> ApplyAsync(
        IUnitOfWork db, TransportDocument doc, NfeSefazResult? result, string signedXml, ILogger? logger,
        ICteAuthorizationHook? authorizationHook = null)
    {
        var wasAuthorized = doc.Status == CteStatus.Authorized;
        doc.UpdatedAt = DateTime.Now;

        if (result is null)
        {
            doc.Status = CteStatus.Processing;
            doc.StatusCode = null;
            doc.StatusReason = NoResponse;
            await db.SaveChangesAsync();
            return new NfeSefazResult(0, NoResponse);
        }

        if (NfeStatusCodes.IsAuthorized(result.StatusCode))
        {
            var signedDigest = NfeProcComposer.SignedDigest(signedXml);
            var protocolDigest = result.ProtocolXml is null ? null : NfeProcComposer.ProtocolDigest(result.ProtocolXml);
            if (signedDigest is null || signedDigest != protocolDigest)
            {
                logger?.LogError(
                    "CT-e {AccessKey}: digVal do protocolo ({ProtocolDigest}) difere do XML enviado ({SignedDigest}).",
                    doc.AccessKey, protocolDigest, signedDigest);
                doc.Status = CteStatus.Processing;
                doc.StatusCode = null;
                doc.StatusReason = DigestMismatch;
                await db.SaveChangesAsync();
                return new NfeSefazResult(0, DigestMismatch);
            }

            doc.Status = CteStatus.Authorized;
            doc.Protocol = result.Protocol;
            doc.AuthorizedAt = result.ReceivedAt?.DateTime;
            if (doc.Xmls.All(x => x.Kind != CteXmlKind.Authorized))
                db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
                {
                    TransportDocumentKey = doc.Key, Kind = CteXmlKind.Authorized, Xml = Proc(signedXml, result.ProtocolXml!),
                });
        }
        else
        {
            doc.Status = NfeStatusCodes.IsDenied(result.StatusCode) ? CteStatus.Denied : CteStatus.Rejected;
        }

        doc.StatusCode = result.StatusCode.ToString();
        doc.StatusReason = result.Reason.Length > 500 ? result.Reason[..500] : result.Reason;
        await db.SaveChangesAsync();

        // Único ponto em que o CT-e vira Autorizado (transmissão e consulta): o gancho roda uma vez, só na transição.
        if (!wasAuthorized && doc.Status == CteStatus.Authorized && authorizationHook is not null)
            await authorizationHook.OnAuthorizedAsync(doc);
        return result;
    }

    /// <summary><c>cteProc</c> = CT-e assinado + <c>protCTe</c>, por texto (a parte assinada fica byte a byte igual).</summary>
    private static string Proc(string signedXml, string protocolXml) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        $"<cteProc versao=\"4.00\" xmlns=\"{CteNamespace}\">" + Body(signedXml) + Body(protocolXml) + "</cteProc>";

    private static string Body(string xml)
    {
        var text = xml.Trim();
        if (text.StartsWith("<?xml", StringComparison.Ordinal))
            text = text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..].Trim();
        return text;
    }
}
