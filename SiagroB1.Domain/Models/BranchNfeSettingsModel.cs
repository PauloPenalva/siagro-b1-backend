using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>O que a tela "Configuração da NF-e" vê. Sem senha e sem o .pfx, de propósito.</summary>
public class BranchNfeSettingsModel
{
    public string BranchCode { get; set; } = string.Empty;
    public NfeEnvironment Environment { get; set; }
    public int Series { get; set; }
    public int NextNumber { get; set; }
    public bool HasCertificate { get; set; }
    public string? CertificateSubject { get; set; }
    public string? CertificateTaxId { get; set; }
    public DateTime? CertificateValidUntil { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>O servidor tem <c>Nfe:CertificateKey</c>? Sem ela não há envio de certificado nem emissão.</summary>
    public bool ServerKeyConfigured { get; set; }
}
