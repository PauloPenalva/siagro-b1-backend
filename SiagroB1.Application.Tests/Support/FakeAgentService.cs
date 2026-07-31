using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Fake de <see cref="IAgentService"/> para testes: resolve o Name por Code a partir de um
/// mapa; códigos desconhecidos devolvem null (reproduz o agente que o cadastro — ou o SAP,
/// em modo SAPB1 — não resolve).
/// </summary>
public sealed class FakeAgentService(Dictionary<int, string>? names = null) : IAgentService
{
    private readonly Dictionary<int, string> _names = names ?? new();

    public Task<AgentModel?> GetByIdAsync(int code) => Task.FromResult(
        _names.TryGetValue(code, out var name)
            ? new AgentModel { Code = code, Name = name }
            : null);

    public Task<IEnumerable<AgentModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<AgentModel> CreateAsync(AgentModel model) => throw new NotImplementedException();
    public Task<AgentModel?> UpdateAsync(int code, AgentModel model) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(int code) => throw new NotImplementedException();
    public IQueryable<AgentModel> QueryAll() => throw new NotImplementedException();
}
