using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Web.Actions.ContractDrafts;

/// <summary>Leitura dos parâmetros das actions de minuta. Enums chegam como string.</summary>
public static class ContractDraftActionParameters
{
    public const string InvalidContractTypeMessage = "Tipo de contrato inválido. Use Purchase ou Sales.";
    public const string InvalidDraftTypeMessage = "Tipo de minuta inválido. Use Contract, Amendment ou Termination.";

    public static bool TryGetGuid(ODataActionParameters? p, string name, out Guid value)
    {
        value = Guid.Empty;
        return p is not null && p.TryGetValue(name, out var obj) && obj is Guid g && (value = g) != Guid.Empty;
    }

    public static string? GetString(ODataActionParameters? p, string name) =>
        p is not null && p.TryGetValue(name, out var obj) ? obj?.ToString() : null;

    public static bool TryGetContractType(ODataActionParameters? p, out ContractDraftContractType type) =>
        Enum.TryParse(GetString(p, "ContractType"), true, out type);

    /// <summary>Ausente ⇒ Contract; presente e inválido ⇒ false.</summary>
    public static bool TryGetDraftType(ODataActionParameters? p, out ContractDraftType type)
    {
        type = ContractDraftType.Contract;
        var text = GetString(p, "DraftType");
        return string.IsNullOrWhiteSpace(text) || Enum.TryParse(text, true, out type);
    }
}
