using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class ProcessingCostsController(IProcessingCostService service)
    : ODataBaseController<ProcessingCost, string>(service)
{
    
}
