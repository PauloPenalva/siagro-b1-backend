using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Fake de <see cref="IWarehouseService"/> para testes: resolve o Name por Code a partir de
/// um mapa; códigos desconhecidos devolvem null.
/// </summary>
public sealed class FakeWarehouseService(Dictionary<string, string>? names = null) : IWarehouseService
{
    private readonly Dictionary<string, string> _names = names ?? new();

    public Task<WarehouseModel?> GetByIdAsync(string code) => Task.FromResult(
        _names.TryGetValue(code, out var name)
            ? new WarehouseModel { Code = code, Name = name }
            : null);

    public Task<IEnumerable<WarehouseModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<WarehouseModel> CreateAsync(WarehouseModel model) => throw new NotImplementedException();
    public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
    public IQueryable<WarehouseModel> QueryAll() => throw new NotImplementedException();
    public Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync() => throw new NotImplementedException();
}
