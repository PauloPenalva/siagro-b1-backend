using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers.SAP
{
    public class ItemsController(IItemService service) 
        : ODataBaseController<Item, string>(service)
    {
        
    }
}