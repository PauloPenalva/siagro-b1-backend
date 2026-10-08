using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services;

/// <summary>
/// Tipo do endereço do parceiro, herdado do CRD1 do SAP. NF-e, CFOP, minuta e endereço do armazém
/// filtram por esses dois valores — outra letra passaria calada e quebraria todos eles.
/// </summary>
public static class AddressTypes
{
    /// <summary>Cobrança (faturamento).</summary>
    public const string BillTo = "B";

    /// <summary>Entrega.</summary>
    public const string ShipTo = "S";

    public static void EnsureValid(string? adresType, IStringLocalizer<Resource> resource)
    {
        if (adresType is not (BillTo or ShipTo))
            throw new DefaultException(resource["BP_ADDRESS_INVALID_TYPE"]);
    }
}
