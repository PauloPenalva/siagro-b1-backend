using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsAttachmentsController(
    PurchaseContractsAttachmentsGetService service) : ODataController
{
    
}