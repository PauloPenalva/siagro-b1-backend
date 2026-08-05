using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// O saldo real do lote vem de SQL puro (Dapper), que o provider InMemory não traduz.
/// Nos testes interessa a decisão dos guards, não a tradução da query — por isso o
/// valor é injetado.
/// </summary>
public sealed class FakeStorageAddressBalanceReader(decimal balance) : IStorageAddressBalanceReader
{
    public decimal GetBalance(string storageAddressCode) => balance;
}
