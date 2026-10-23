using SiagroB1.Application.Services.Ecf;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>Ajustes do LALUR/LACS da ECF (M300/M350). Só STANDALONE.</summary>
public class EcfAdjustmentsController(EcfAdjustmentService service, IConfiguration configuration)
    : EcfCrudController<EcfAdjustment>(service, configuration);
