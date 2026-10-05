using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace SiagroB1.Fiscal.Certificates;

public sealed record CertificateInfo(
    string Subject, string? TaxId, DateTime ValidFrom, DateTime ValidUntil, bool HasPrivateKey);

/// <summary>
/// Lê do certificado o que a tela mostra e o envio confere: titular, CNPJ, validade e se há chave
/// privada. O CNPJ vem do SubjectAlternativeName (otherName OID 2.16.76.1.3.3 da ICP-Brasil); na
/// falta dele, do fim do CN ("RAZAO SOCIAL:CNPJ"), que é o formato usual das AC brasileiras.
/// </summary>
public static class CertificateInspector
{
    public const string IcpBrasilCnpjOid = "2.16.76.1.3.3";

    private static readonly Regex TaxIdPattern = new("^[0-9A-Z]{14}$");

    public static CertificateInfo Inspect(byte[] pfx, string password)
    {
        using var certificate = CertificateLoader.Load(pfx, password);
        return Inspect(certificate);
    }

    public static CertificateInfo Inspect(X509Certificate2 certificate) =>
        new(certificate.Subject,
            ReadIcpBrasilCnpj(certificate) ?? ReadFromCommonName(certificate),
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.HasPrivateKey);

    private static string? ReadIcpBrasilCnpj(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions["2.5.29.17"];

        if (extension is null)
            return null;

        try
        {
            var names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            var otherNameTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);

            while (names.HasData)
            {
                if (!names.PeekTag().HasSameClassAndValue(otherNameTag))
                {
                    names.ReadEncodedValue();
                    continue;
                }

                var otherName = names.ReadSequence(otherNameTag);
                var oid = otherName.ReadObjectIdentifier();
                var value = otherName.ReadSequence(otherNameTag);

                if (oid != IcpBrasilCnpjOid)
                    continue;

                var text = value.PeekTag().TagValue switch
                {
                    (int)UniversalTagNumber.OctetString => Encoding.ASCII.GetString(value.ReadOctetString()),
                    (int)UniversalTagNumber.UTF8String => value.ReadCharacterString(UniversalTagNumber.UTF8String),
                    (int)UniversalTagNumber.PrintableString => value.ReadCharacterString(UniversalTagNumber.PrintableString),
                    (int)UniversalTagNumber.IA5String => value.ReadCharacterString(UniversalTagNumber.IA5String),
                    _ => null,
                };

                return Clean(text);
            }
        }
        catch (AsnContentException)
        {
            // SAN fora do padrão: tenta o CN.
        }

        return null;
    }

    private static string? ReadFromCommonName(X509Certificate2 certificate)
    {
        var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var separator = commonName.LastIndexOf(':');

        return separator < 0 ? null : Clean(commonName[(separator + 1)..]);
    }

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;

        var cleaned = new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

        return TaxIdPattern.IsMatch(cleaned) ? cleaned : null;
    }
}
