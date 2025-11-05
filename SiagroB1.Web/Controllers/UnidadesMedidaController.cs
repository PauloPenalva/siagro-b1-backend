using SiagroB1.Core.Interfaces;
using SiagroB1.Web.Base;
using UnitOfMeasure = SiagroB1.Domain.Entities.UnitOfMeasure;

namespace SiagroB1.Web.Controllers
{
    public class UnidadesMedidaController(IUnitOfMeasureService service) 
        : ODataBaseController<UnitOfMeasure, string>(service)
    {
        
    }
}