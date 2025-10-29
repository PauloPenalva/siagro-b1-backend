using SiagroB1.Domain.Entities;
using SiagroB1.Core.Services;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers
{
    public class ContasContabeisController(IContaContabilService service) 
        : ODataBaseController<ContaContabil, int>(service)
    {
    }
}