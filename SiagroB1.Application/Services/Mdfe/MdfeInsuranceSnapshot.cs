using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// O seguro da carga é copiado da configuração de transporte da filial para o MDF-e. Se a configuração mudar depois
/// da criação, o MDF-e ainda sem seguro completo recebe a cópia nova (gravação, confirmação, transmissão).
/// </summary>
public static class MdfeInsuranceSnapshot
{
    /// <summary>Seguradora com CNPJ de 14 dígitos, nome, apólice e responsável.</summary>
    public static bool IsComplete(TransportManifest m) =>
        !string.IsNullOrWhiteSpace(m.InsuranceResponsible)
        && MdfeReadinessValidator.Digits(m.InsurerTaxId).Length == 14
        && !string.IsNullOrWhiteSpace(m.InsurerName)
        && !string.IsNullOrWhiteSpace(m.PolicyNumber);

    public static void Apply(TransportManifest m, BranchTransportSettings? transport)
    {
        m.InsuranceResponsible = transport?.InsuranceResponsible is { } r ? ((int)r).ToString() : null;
        m.InsurerTaxId = transport?.InsurerTaxId;
        m.InsurerName = SiagroB1.Fiscal.Dfe.DfeText.CleanOrNull(transport?.InsurerName, 30);
        m.PolicyNumber = transport?.PolicyNumber;
    }

    /// <summary>Copia da filial só quando o seguro do MDF-e está incompleto e a filial tem algo a oferecer.</summary>
    public static void RefreshIfIncomplete(TransportManifest m, BranchTransportSettings? transport)
    {
        if (IsComplete(m) || transport is null) return;
        Apply(m, transport);
    }
}
