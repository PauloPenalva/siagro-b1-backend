using Microsoft.AspNetCore.Mvc;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsController(IPurchaseContractService service) 
    : ODataBaseController<PurchaseContract, Guid>(service)
{
    [HttpGet("odata/PurchaseContracts({key})/PriceFixations")]
    [HttpGet("odata/PurchaseContracts/{key}/PriceFixations")]
    public ICollection<PurchaseContractPriceFixation> GetPriceFixations([FromRoute] Guid key)
    {
        return new List<PurchaseContractPriceFixation>();
    }
    
    [HttpGet("odata/PurchaseContracts({key})/PriceFixations({fixationKey})")]
    [HttpGet("odata/PurchaseContracts/{key}/PriceFixations/{fixationKey}")]
    public PurchaseContractPriceFixation GetPriceFixationByKey([FromRoute] Guid key, [FromRoute] string fixationKey)
    {
        return null;
    }
}