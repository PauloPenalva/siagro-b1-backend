using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SiagroB1.Fiscal.Tests.Support;

/// <summary>
/// Certificado A1 de mentira, gerado no teste: RSA 2048 autoassinado, com o CNPJ no
/// SubjectAlternativeName (otherName OID 2.16.76.1.3.3, como na ICP-Brasil) e no fim do CN
/// ("RAZAO:CNPJ"). Serve para assinar o XML e validar no XSD sem certificado real.
/// Compartilhado com o SiagroB1.Application.Tests por link no .csproj.
/// </summary>
public static class TestCertificates
{
    public const string Password = "senha-teste";

    public static byte[] CreatePfx(
        string? icpBrasilCnpj = "12345678000195",
        string commonName = "CEAGUI CEREAIS LTDA:12345678000195",
        DateTimeOffset? notAfter = null,
        bool withPrivateKey = true,
        string password = Password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (icpBrasilCnpj is not null)
            request.CertificateExtensions.Add(IcpBrasilSubjectAlternativeName(icpBrasilCnpj));

        var end = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
        using var certificate = request.CreateSelfSigned(end.AddYears(-2), end);

        if (withPrivateKey)
            return certificate.Export(X509ContentType.Pfx, password);

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pfx, password);
    }

    private static X509Extension IcpBrasilSubjectAlternativeName(string cnpj)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        {
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            {
                writer.WriteObjectIdentifier("2.16.76.1.3.3");

                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    writer.WriteOctetString(Encoding.ASCII.GetBytes(cnpj));
                }
            }
        }

        return new X509Extension("2.5.29.17", writer.Encode(), critical: false);
    }
}
