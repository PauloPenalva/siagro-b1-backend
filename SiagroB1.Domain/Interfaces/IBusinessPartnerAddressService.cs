using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IBusinessPartnerAddressService
{
    IQueryable<AddressModel> QueryAll(string cardCode);
    Task<AddressModel?> GetByIdAsync(string cardCode, string addressName, string adresType);
    Task<AddressModel> Create(string cardCode, AddressModel addressModel);
    Task<AddressModel> Update(string cardCode, string addressName, string adresType, AddressModel addressModel);
    Task<bool> Delete(string cardCode, string addressName, string adresType);
}