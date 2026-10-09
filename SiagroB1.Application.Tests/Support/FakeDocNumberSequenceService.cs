using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Duplo de <see cref="DocNumberSequenceService"/> para testes de orquestração: devolve
/// um código sequencial em memória. O serviço real roda em T-SQL puro (Dapper, com
/// <c>OUTPUT</c>/<c>UPDLOCK</c>), que o provider InMemory não traduz — e a numeração não
/// é o que estes testes verificam.
/// </summary>
public sealed class FakeDocNumberSequenceService(Guid? docNumberKey = null)
    : DocNumberSequenceService(null!)
{
    private readonly Guid _docNumberKey = docNumberKey ?? Guid.NewGuid();
    private int _next = 1;

    /// <summary>Códigos de transação pedidos, em ordem (para provar QUAL numeração foi usada).</summary>
    public List<TransactionCode> RequestedCodes { get; } = [];

    /// <summary>Quantas vezes um número foi consumido.</summary>
    public int NumberCalls { get; private set; }

    public override Task<string> GetDocNumber(Guid key) =>
        Task.FromResult($"ST-{(NumberCalls++, _next++).Item2:0000}");

    public override Task<Guid> GetKeyByTransactionCode(TransactionCode transactionCode)
    {
        RequestedCodes.Add(transactionCode);
        return Task.FromResult(_docNumberKey);
    }
}
