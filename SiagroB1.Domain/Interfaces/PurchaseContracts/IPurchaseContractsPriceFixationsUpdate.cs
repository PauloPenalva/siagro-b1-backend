using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Interfaces.PurchaseContracts;

public interface IPurchaseContractsPriceFixationsUpdate 
    : IActionAssociationEntityUpdate<PurchaseContractPriceFixation, Guid, Guid>
{
    
}