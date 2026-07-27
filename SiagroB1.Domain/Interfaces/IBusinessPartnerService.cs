using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IBusinessPartnerService
{
    Task<IEnumerable<BusinessPartnerModel>> GetAllAsync();
    Task<BusinessPartnerModel?> GetByIdAsync(string code);
    Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel entity);
    Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel entity);
    Task<bool> DeleteAsync(string code);
    IQueryable<BusinessPartnerModel> QueryAll();
    Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null);
    /// <summary>
    /// Dados de fornecedor (nome, CNPJ, endereço, observação) dos <paramref name="cardCodes"/>
    /// informados, indexados por CardCode. Códigos sem parceiro correspondente ficam de fora.
    /// </summary>
    Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(IReadOnlyCollection<string> cardCodes);
}