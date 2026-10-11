namespace SiagroB1.Domain.Enums;

/// <summary>
/// Papel da natureza na remessa para depósito em armazém de terceiro (spec 2026-10-10). Remessa é o nosso documento
/// de saída para o armazém; Retorno é a NF-e que o armazém emite a cada carregamento. Nenhum dos dois mexe no
/// contrato.
/// </summary>
public enum StorageDepositRole
{
    None = 0,
    Remittance = 1,
    Return = 2,
}
