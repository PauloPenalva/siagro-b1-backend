namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Controle Ativo/Inativo do SAP B1, igual em OCRD e OITM: <c>validFor</c>/<c>frozenFor</c> com
/// intervalo de datas opcional para cada um.
/// </summary>
public interface ISapActivityFlags
{
    string? ValidFor { get; }
    DateTime? ValidFrom { get; }
    DateTime? ValidTo { get; }
    string? FrozenFor { get; }
    DateTime? FrozenFrom { get; }
    DateTime? FrozenTo { get; }
}
