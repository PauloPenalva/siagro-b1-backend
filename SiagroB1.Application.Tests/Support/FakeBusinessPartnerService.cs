using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Fake de <see cref="IBusinessPartnerService"/> para testes: resolve o CardName por
/// CardCode a partir de um mapa; códigos desconhecidos devolvem null.
/// <paramref name="suppliers"/> alimenta o enriquecimento de fornecedor (CNPJ, cidade,
/// UF, observação); um código ausente reproduz o parceiro que o cadastro não resolve.
/// <paramref name="states"/> alimenta a UF do endereço de faturamento — é dela que a
/// resolução de CFOP decide entre operação dentro e fora do estado. Código ausente do mapa
/// reproduz o parceiro sem UF cadastrada.
/// </summary>
public sealed class FakeBusinessPartnerService(
    Dictionary<string, string>? names = null,
    Dictionary<string, SupplierInfo>? suppliers = null,
    Dictionary<string, string>? states = null,
    Dictionary<string, List<AddressModel>>? addresses = null,
    Dictionary<string, string>? taxIds = null) : IBusinessPartnerService
{
    /// <summary>CNPJ/CPF por CardCode — usado pela importação do XML, que resolve o emitente.</summary>
    private readonly Dictionary<string, string> _taxIds = taxIds ?? new();

    private readonly Dictionary<string, string> _names = names ?? new();
    private readonly Dictionary<string, SupplierInfo> _suppliers = suppliers ?? new();
    private readonly Dictionary<string, string> _states = states ?? new();

    /// <summary>
    /// Coleção completa de endereços, para exercitar a ESCOLHA entre eles — é o que o modo
    /// STANDALONE entrega (em SAPB1 vem só o de faturamento). Tem precedência sobre
    /// <paramref name="states"/>, que é o atalho de um endereço só.
    /// </summary>
    private readonly Dictionary<string, List<AddressModel>> _addresses = addresses ?? new();

    public Task<BusinessPartnerModel?> GetByIdAsync(string code) => Task.FromResult(
        _names.TryGetValue(code, out var name)
            ? new BusinessPartnerModel
            {
                CardCode = code,
                CardName = name,
                Addresses = ResolveAddresses(code),
            }
            : null);

    private List<AddressModel> ResolveAddresses(string code)
    {
        if (_addresses.TryGetValue(code, out var list))
        {
            return list;
        }

        return _states.TryGetValue(code, out var state)
            ?
            [
                new AddressModel
                {
                    CardCode = code,
                    AddressName = "FATURAMENTO",
                    AdresType = "B",
                    State = state,
                }
            ]
            : [];
    }

    public Task<IEnumerable<BusinessPartnerModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel entity) => throw new NotImplementedException();
    public Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel entity) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
    public IQueryable<BusinessPartnerModel> QueryAll() =>
        _names.Select(kv => new BusinessPartnerModel
        {
            CardCode = kv.Key,
            CardName = kv.Value,
            TaxId = _taxIds.GetValueOrDefault(kv.Key),
        }).AsQueryable();
    public Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null) => throw new NotImplementedException();
    public Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(IReadOnlyCollection<string> cardCodes) =>
        Task.FromResult(_suppliers
            .Where(kv => cardCodes.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
