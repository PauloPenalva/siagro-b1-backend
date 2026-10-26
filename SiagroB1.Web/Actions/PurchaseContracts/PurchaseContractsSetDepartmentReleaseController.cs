using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsSetDepartmentReleaseController(
    PurchaseContractsSetDepartmentReleaseService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsSetDepartmentRelease")]
    public async Task<IActionResult> SetDepartmentReleaseAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // O OData entrega NULO aqui quando o corpo não traz nenhum parâmetro declarado no EDM.
            if (parameters is null
                || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null
                || !parameters.TryGetValue("Released", out var releasedObj) || releasedObj is null)
                return BadRequest("Missing required parameters");

            // Parâmetro de string é anulável mesmo quando presente.
            parameters.TryGetValue("Department", out var deptObj);
            var deptText = deptObj?.ToString();
            if (string.IsNullOrWhiteSpace(deptText)
                || !Enum.TryParse<PaymentReleaseDepartment>(deptText, out var department)
                || !Enum.IsDefined(department))
                return BadRequest("Departamento de liberação inválido.");

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString()!);
            var released = Convert.ToBoolean(releasedObj);
            await service.ExecuteAsync(key, department, released, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
