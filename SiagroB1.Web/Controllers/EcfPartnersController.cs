using SiagroB1.Application.Services.Ecf;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>Quadro societário da ECF (Y600). Só STANDALONE.</summary>
public class EcfPartnersController(EcfPartnerService service, IConfiguration configuration)
    : EcfCrudController<EcfPartner>(service, configuration);
