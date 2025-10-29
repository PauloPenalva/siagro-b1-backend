using SiagroB1.Domain.Entities;
using SiagroB1.Core.Interfaces;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers
{
    public class QualidadesController(ITabelaCustoQualidadeService service) 
        : ODataBaseController<TabelaCustoQualidade, int>(service)
    {
        
    }
}