using DFe.Utils;
using DFe.Utils.Assinatura;
using MDFe.Classes.Extensoes;
using SiagroB1.Fiscal.Nfe;
using ZeusMdfe = MDFe.Classes.Informacoes.MDFe;

namespace SiagroB1.Fiscal.Mdfe;

/// <summary>MDF-e assinado e validado no XSD, pronto para gravar e transmitir.</summary>
public sealed record SignedMdfe(string Xml, string AccessKey);

/// <summary>
/// Assina com o certificado já aberto da filial e valida no XSD do MDF-e 3.00 (e do modal
/// rodoviário). Falha de schema vira <see cref="NfeValidationException"/>, como no CT-e.
/// </summary>
public static class MdfeSigner
{
    public static SignedMdfe SignAndValidate(ZeusMdfe mdfe, MdfeServiceSettings settings)
    {
        MdfeZeusConfiguration.PinGlobalLayout();
        var configuration = MdfeZeusConfiguration.Create(settings);

        // Não ExtMDFe.Assina: ela relê o certificado de cfg.ConfiguracaoCertificado.
        var defaults = new ConfiguracaoCertificado();
        mdfe.Signature = AssinaturaDigital.Assina(
            mdfe, mdfe.InfMDFe.Id, settings.Certificate,
            defaults.SignatureMethodSignedXml, defaults.DigestMethodReference, false);

        try
        {
            mdfe.Valida(configuration);
        }
        catch (Exception e) when (e.Message.StartsWith("Erros da valida", StringComparison.Ordinal))
        {
            // A Zeus MDF-e lança Exception crua ("Erros da validação : ...") na falha de XSD.
            throw new NfeValidationException(e.Message);
        }

        return new SignedMdfe(mdfe.XmlString(), mdfe.InfMDFe.Id[4..]);
    }
}
