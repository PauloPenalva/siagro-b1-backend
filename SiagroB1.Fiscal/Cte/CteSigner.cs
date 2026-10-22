using CTe.Utils.CTe;
using DFe.Utils;
using DFe.Utils.Assinatura;
using SiagroB1.Fiscal.Nfe;
using ZeusCte = CTe.Classes.CTe;

namespace SiagroB1.Fiscal.Cte;

/// <summary>CT-e assinado e validado no XSD, pronto para gravar e transmitir.</summary>
public sealed record SignedCte(string Xml, string AccessKey);

/// <summary>
/// Assina com o certificado da filial e valida no XSD do CT-e 4.00. Falha de schema vira
/// <see cref="NfeValidationException"/> — a mesma da NF-e, para reaproveitar o tratamento.
/// </summary>
public static class CteSigner
{
    public static SignedCte SignAndValidate(ZeusCte cte, CteServiceSettings settings)
    {
        CteZeusConfiguration.PinGlobalLayout();
        var configuration = CteZeusConfiguration.Create(settings);

        // A Zeus CT-e só assina a partir de ConfiguracaoCertificado (arquivo, repositório ou
        // bytes); o certificado já aberto pelo chamador vai direto ao assinador do DFe.
        var defaults = new ConfiguracaoCertificado();
        cte.Signature = AssinaturaDigital.Assina(
            cte, cte.infCte.Id, settings.Certificate,
            defaults.SignatureMethodSignedXml, defaults.DigestMethodReference, false);

        try
        {
            cte.ValidaSchema(configuration);
        }
        catch (Exception e) when (e.Message.StartsWith("Erros da valida", StringComparison.Ordinal))
        {
            // A Zeus CT-e lança Exception crua ("Erros da validação : ...") na falha de XSD.
            throw new NfeValidationException(e.Message);
        }

        return new SignedCte(cte.ObterXmlString(), cte.infCte.Id[3..]);
    }
}
