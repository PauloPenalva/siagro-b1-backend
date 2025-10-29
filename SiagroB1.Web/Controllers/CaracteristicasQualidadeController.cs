using SiagroB1.Domain.Entities;
using SiagroB1.Core.Interfaces;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers
{
    public class CaracteristicasQualidadeController(ICaracteristicaQualidadeService service) 
        : ODataBaseController<CaracteristicaQualidade, int>(service)
    {
        
    }
}