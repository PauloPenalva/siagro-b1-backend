using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Web.Functions.DocNumbers;

public class DocNumberGetInfoByTransactionCodeController(
    DocNumberGetInfoByTransactionCodeService service
    ) : ODataController
{
    [HttpGet("odata/DocNumberGetInfoByTransaction(Transaction={code})")]
    public async Task<ActionResult<ICollection<DocNumberInfoDto>>> List([FromRoute] TransactionCode code)
    {
        return Ok(await service.GetInfo(code));
    }
}