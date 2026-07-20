namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Leitura do saldo de um lote de armazenagem. Existe para que os guards que dependem
/// do saldo possam ser testados sem SQL Server — a implementação real é Dapper/T-SQL.
/// </summary>
public interface IStorageAddressBalanceReader
{
    decimal GetBalance(string storageAddressCode);
}
