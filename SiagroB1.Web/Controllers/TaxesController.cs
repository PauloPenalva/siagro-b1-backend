using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class TaxesController(ITaxService service) 
    : ODataBaseController<Tax, string>(service)
{
    
}