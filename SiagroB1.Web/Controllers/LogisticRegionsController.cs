using SiagroB1.Application.Services;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class LogisticRegionsController(LogisticRegionService service) 
    : ODataBaseController<LogisticRegion, string>(service)
{
    
}