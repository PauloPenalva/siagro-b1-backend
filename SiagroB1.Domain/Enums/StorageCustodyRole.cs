namespace SiagroB1.Domain.Enums;

/// <summary>
/// Papel da natureza nas notas de custódia em armazém PRÓPRIO, em que somos o depositário (spec 2026-10-12 §3.1).
/// Espelho de <see cref="StorageDepositRole"/>, que é o caso oposto (nosso grão em armazém de terceiro) — não misturar.
/// Entradas (1 e 3) são Documento de Entrada; devolução e retornos (2, 4 e 5), Documento de Saída. Nenhum mexe em
/// contrato.
/// </summary>
public enum StorageCustodyRole
{
    None = 0,
    /// <summary>Entrada para beneficiamento (CFOP 1.901/2.901).</summary>
    ProcessingEntry = 1,
    /// <summary>Devolução simbólica do beneficiamento (5.902/6.902), com NFref para a entrada.</summary>
    ProcessingSymbolicReturn = 2,
    /// <summary>Entrada (simbólica ou física) para armazenagem (1.905/2.905).</summary>
    StorageEntry = 3,
    /// <summary>Retorno de armazenagem na retirada (5.906/6.906).</summary>
    StorageReturn = 4,
    /// <summary>Retorno simbólico de armazenagem na transferência (5.907/6.907).</summary>
    StorageSymbolicReturn = 5,
}
