using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Fake de <see cref="IBusinessPartnerService"/> para testes: resolve o CardName por
/// CardCode a partir de um mapa; códigos desconhecidos devolvem null.
/// </summary>
public sealed class FakeBusinessPartnerService(Dictionary<string, string>? names = null) : IBusinessPartnerService
{
    private readonly Dictionary<string, string> _names = names ?? new();

    public Task<BusinessPartnerModel?> GetByIdAsync(string code) => Task.FromResult(
        _names.TryGetValue(code, out var name)
            ? new BusinessPartnerModel { CardCode = code, CardName = name }
            : null);

    public Task<IEnumerable<BusinessPartnerModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel entity) => throw new NotImplementedException();
    public Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel entity) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
    public IQueryable<BusinessPartnerModel> QueryAll() => throw new NotImplementedException();
    public Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null) => throw new NotImplementedException();
    public Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync() => throw new NotImplementedException();
}
