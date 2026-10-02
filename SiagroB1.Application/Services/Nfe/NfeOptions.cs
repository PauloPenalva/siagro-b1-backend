using System.Globalization;
using Microsoft.Extensions.Configuration;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Leitura única da seção <c>Nfe</c> do appsettings (e do modo de integração).</summary>
public class NfeOptions(IConfiguration configuration)
{
    public bool IsStandalone => ErpMode.IsStandalone(configuration);

    public bool HasCertificateKey => !string.IsNullOrWhiteSpace(configuration[CertificatePasswordCipher.ConfigurationKey]);

    public CertificatePasswordCipher Cipher() =>
        CertificatePasswordCipher.FromBase64(configuration[CertificatePasswordCipher.ConfigurationKey]);

    public int TimeoutMilliseconds =>
        (int.TryParse(configuration["Nfe:TimeoutSeconds"], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : 60) * 1000;

    public bool ValidateSefazCertificate =>
        !bool.TryParse(configuration["Nfe:ValidateSefazCertificate"], out var validate) || validate;

    public NfeTechnicalResponsible? TechnicalResponsible
    {
        get
        {
            var cnpj = configuration["Nfe:TechnicalResponsible:Cnpj"];

            return string.IsNullOrWhiteSpace(cnpj)
                ? null
                : new NfeTechnicalResponsible(
                    NfeText.AlphaNumeric(cnpj),
                    configuration["Nfe:TechnicalResponsible:Contact"] ?? string.Empty,
                    configuration["Nfe:TechnicalResponsible:Email"] ?? string.Empty,
                    NfeText.Digits(configuration["Nfe:TechnicalResponsible:Phone"]));
        }
    }
}
