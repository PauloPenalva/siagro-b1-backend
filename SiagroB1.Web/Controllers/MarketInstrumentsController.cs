using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;
using SiagroB1.Web.Filters;

namespace SiagroB1.Web.Controllers;

[MarketModuleEnabled]
public class MarketInstrumentsController(MarketInstrumentService service)
    : ODataBaseController<MarketInstrument, string>(service)
{
}
