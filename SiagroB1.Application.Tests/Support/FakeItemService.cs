using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Fake de <see cref="IItemService"/> para testes: resolve o ItemName por ItemCode a
/// partir de um mapa; códigos desconhecidos devolvem null (reproduz o produto que o
/// cadastro — ou o SAP, em modo SAPB1 — não resolve).
/// </summary>
public sealed class FakeItemService(Dictionary<string, string>? names = null) : IItemService
{
    private readonly Dictionary<string, string> _names = names ?? new();

    public Task<ItemModel?> GetByIdAsync(string code) => Task.FromResult(
        _names.TryGetValue(code, out var name)
            ? new ItemModel { ItemCode = code, ItemName = name }
            : null);

    public Task<IEnumerable<ItemModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<ItemModel> CreateAsync(ItemModel entity) => throw new NotImplementedException();
    public Task<ItemModel?> UpdateAsync(string code, ItemModel entity) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
    public IQueryable<ItemModel> QueryAll() => throw new NotImplementedException();
    public Task<bool> DeleteAsyncWithTransaction(string code, Func<ItemModel, Task>? preDeleteAction = null) =>
        throw new NotImplementedException();
}
