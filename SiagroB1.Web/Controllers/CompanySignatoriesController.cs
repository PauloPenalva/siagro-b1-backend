using SiagroB1.Application.Services.CompanySignatories;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class CompanySignatoriesController(CompanySignatoryService service)
    : ODataBaseController<CompanySignatory, Guid>(service)
{
}
