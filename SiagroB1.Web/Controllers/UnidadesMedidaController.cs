using SiagroB1.Domain.Entities;
using SiagroB1.Core.Services;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers
{
    public class UnidadesMedidaController(IUnidadeMedidaService service) 
        : ODataBaseController<UnidadeMedida, string>(service)
    {
        
    }
}