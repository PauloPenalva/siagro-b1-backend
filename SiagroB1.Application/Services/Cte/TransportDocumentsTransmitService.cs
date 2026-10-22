using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Transmite o CT-e (Confirmado ou Rejeitado) com o número e a chave fixados na confirmação. Sem resposta da SEFAZ
/// (tempo esgotado) ou com duplicidade (204/539), consulta pela chave em vez de reenviar: se já foi autorizado, vale
/// como autorizado.
/// </summary>
public class TransportDocumentsTransmitService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    CteServiceContextFactory contexts,
    ICteSefazClient sefaz,
    NfeOptions options,
    Func<DateTimeOffset>? clock = null)
{
    public const string NotTransmittable = "Só é possível transmitir CT-e confirmado ou rejeitado.";
    public const string NoResponse = "Sem resposta da SEFAZ: consulte depois.";

    public async Task<NfeSefazResult> TransmitAsync(Guid key, string user)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await CteEmission.LoadAsync(db, key);
        if (doc.Status is not (CteStatus.Confirmed or CteStatus.Rejected))
            throw new DefaultException(NotTransmittable);
        if (doc.AccessKey is null)
            throw new DefaultException("Confirme o CT-e antes de transmitir: ele ainda não tem número e chave.");

        // A data de emissão é renovada, mas o AAMM da chave foi fixado na confirmação.
        var now = TimeZoneInfo.ConvertTime(clock?.Invoke() ?? DateTimeOffset.UtcNow, NfeIssueInputAssembler.BrasiliaZone);
        var keyMonth = doc.AccessKey.Substring(2, 4);
        if (now.ToString("yyMM") != keyMonth)
            throw new DefaultException(
                $"A chave foi gerada em {keyMonth[2..]}/20{keyMonth[..2]} e a emissão cairia em outro mês. Descarte o número e confirme de novo.");

        doc.IssuedAt = now.DateTime;

        var settings = await db.Context.BranchCteSettings.AsNoTracking().FirstAsync(x => x.BranchCode == doc.BranchCode);
        var environment = doc.Environment ?? settings.Environment;
        using var context = await contexts.OpenAsync(doc.BranchCode, environment);
        var signed = await CteEmission.SignAsync(db, doc, settings, context.Settings, options);

        doc.Status = CteStatus.Processing;
        doc.UpdatedAt = DateTime.Now;
        doc.UpdatedBy = user;
        await db.SaveChangesAsync();

        NfeSefazResult result;
        try
        {
            result = await sefaz.AuthorizeAsync(signed, context.Settings);
            if (NfeStatusCodes.IsDuplicate(result.StatusCode))
                result = await RecoverAsync(signed.AccessKey, context.Settings);
        }
        catch (NfeCommunicationException)
        {
            result = await RecoverAsync(signed.AccessKey, context.Settings);
        }

        await CteEmission.ApplyAsync(db, doc, result, signed.Xml);
        return result;
    }

    /// <summary>A situação real é desconhecida: só a consulta pela chave diz se a SEFAZ autorizou.</summary>
    private async Task<NfeSefazResult> RecoverAsync(string accessKey, CteServiceSettings settings)
    {
        try
        {
            var consult = await sefaz.ConsultProtocolAsync(accessKey, settings);
            if (NfeStatusCodes.IsAuthorized(consult.StatusCode))
                return consult;
        }
        catch (NfeCommunicationException)
        {
        }

        return new NfeSefazResult(0, NoResponse);
    }
}

/// <summary>Partes comuns da transmissão e da consulta do CT-e.</summary>
internal static class CteEmission
{
    private const string CteNamespace = "http://www.portalfiscal.inf.br/cte";

    public static async Task<TransportDocument> LoadAsync(IUnitOfWork db, Guid key) =>
        await db.Context.TransportDocuments
            .Include(x => x.Parties).Include(x => x.NfeKeys).Include(x => x.Quantities).Include(x => x.Components)
            .Include(x => x.Xmls)
            .FirstOrDefaultAsync(x => x.Key == key)
        ?? throw new DefaultException("CT-e não encontrado.");

    /// <summary>
    /// Monta e assina a partir do documento gravado. A assinatura RSA é determinística: o mesmo documento (mesma
    /// data de emissão) gera o mesmo XML — é o que a consulta usa para compor o cteProc de um CT-e recuperado.
    /// </summary>
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

    public static async Task ApplyAsync(IUnitOfWork db, TransportDocument doc, NfeSefazResult result, string signedXml)
    {
        doc.StatusCode = result.StatusCode == 0 ? null : result.StatusCode.ToString();
        doc.StatusReason = result.Reason.Length > 500 ? result.Reason[..500] : result.Reason;

        if (NfeStatusCodes.IsAuthorized(result.StatusCode))
        {
            doc.Status = CteStatus.Authorized;
            doc.Protocol = result.Protocol;
            doc.AuthorizedAt = result.ReceivedAt?.DateTime;
            if (result.ProtocolXml is not null && doc.Xmls.All(x => x.Kind != CteXmlKind.Authorized))
                db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
                {
                    TransportDocumentKey = doc.Key, Kind = CteXmlKind.Authorized, Xml = Proc(signedXml, result.ProtocolXml),
                });
        }
        else
        {
            doc.Status = NfeStatusCodes.IsDenied(result.StatusCode) ? CteStatus.Denied : CteStatus.Rejected;
        }

        doc.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
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
