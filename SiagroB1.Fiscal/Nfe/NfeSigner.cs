using NFe.Utils.Excecoes;
using NFe.Utils.NFe;
using ZeusNFe = NFe.Classes.NFe;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>NF-e assinada e validada, pronta para gravar (antes do envio) e transmitir.</summary>
public sealed class SignedNfe
{
    internal SignedNfe(ZeusNFe document, string xml, string accessKey)
    {
        Document = document;
        Xml = xml;
        AccessKey = accessKey;
    }

    public string Xml { get; }

    /// <summary>Chave de 44 posições (o <c>Id</c> sem o prefixo "NFe").</summary>
    public string AccessKey { get; }

    internal ZeusNFe Document { get; }
}

/// <summary>Falha de schema na validação local: nada foi enviado à SEFAZ.</summary>
public sealed class NfeValidationException(string message) : Exception(message);

/// <summary>
/// Assina e valida no XSD. A Zeus monta a chave e o <c>cDV</c> na assinatura (inclusive com CNPJ
/// alfanumérico) — por isso o sistema não tem montador de chave próprio.
/// </summary>
public static class NfeSigner
{
    public static SignedNfe BuildSignAndValidate(NfeIssueInput input, NfeServiceSettings settings) =>
        SignAndValidate(NfeXmlBuilder.Build(input), settings);

    public static SignedNfe SignAndValidate(ZeusNFe nfe, NfeServiceSettings settings)
    {
        var configuration = NfeZeusConfiguration.Create(settings);

        try
        {
            nfe.Assina(configuration, settings.Certificate);
            nfe.Valida(configuration);
        }
        catch (ValidacaoSchemaException e)
        {
            throw new NfeValidationException(e.Message);
        }

        return new SignedNfe(nfe, nfe.ObterXmlString(), nfe.infNFe.Id[3..]);
    }
}
