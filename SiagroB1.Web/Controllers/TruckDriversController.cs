using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class TruckDriversController(ITruckDriverService service) 
    : ODataBaseController<TruckDriver, string>(service)
{
    
}
