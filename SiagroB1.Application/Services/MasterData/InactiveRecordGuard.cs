using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Services.MasterData;

/// <summary>
/// GAC-1196: impede ESCOLHER um cadastro inativo no SAP — em registro novo ou numa troca do
/// campo. Não impede gravar um documento que JÁ aponta para ele: o cadastro pode ter sido
/// inativado depois, com o documento em andamento, e esse documento precisa continuar editável
/// até o fim. Por isso todo método recebe o código anterior e só acusa quando ele mudou.
///
/// Devolve o próprio modelo para encaixar na leitura que os serviços já fazem do nome.
/// </summary>
public static class InactiveRecordGuard
{
    public static BusinessPartnerModel? Partner(
        BusinessPartnerModel? partner, string? previousCode = null, string label = "O parceiro")
    {
        if (partner is { Inactive: true } && !SameCode(partner.CardCode, previousCode))
            throw Blocked(label, partner.CardCode, partner.CardName);

        return partner;
    }

    public static ItemModel? Item(ItemModel? item, string? previousCode = null)
    {
        if (item is { Inactive: true } && !SameCode(item.ItemCode, previousCode))
            throw Blocked("O produto", item.ItemCode, item.ItemName);

        return item;
    }

    public static WarehouseModel? Warehouse(
        WarehouseModel? warehouse, string? previousCode = null, string label = "O armazém")
    {
        if (warehouse is { Inactive: true } && !SameCode(warehouse.Code, previousCode))
            throw Blocked(label, warehouse.Code, warehouse.Name);

        return warehouse;
    }

    public static AgentModel? Agent(AgentModel? agent, int? previousCode = null)
    {
        if (agent is { Inactive: "Y" } && agent.Code != previousCode)
            throw Blocked("O representante", agent.Code.ToString(), agent.Name);

        return agent;
    }

    /// <summary>
    /// Valor gravado no banco antes desta edição. Tem de vir do rastreador: no PATCH o controller
    /// aplica o delta sobre a entidade rastreada, então a propriedade já chega com o valor novo.
    /// Em registro novo não há valor anterior.
    /// </summary>
    public static TValue? Original<TValue>(EntityEntry entry, string propertyName) =>
        entry.State is EntityState.Added or EntityState.Detached
            ? default
            : entry.OriginalValues.GetValue<TValue?>(propertyName);

    private static bool SameCode(string? code, string? previousCode) =>
        previousCode != null && string.Equals(code?.Trim(), previousCode.Trim(), StringComparison.OrdinalIgnoreCase);

    private static DefaultException Blocked(string label, string code, string? name) =>
        new($"{label} {code} - {name} está inativo no SAP e não pode ser usado.");
}
