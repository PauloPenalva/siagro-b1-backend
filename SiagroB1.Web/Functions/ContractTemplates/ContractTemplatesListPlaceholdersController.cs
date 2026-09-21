using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Web.Functions.ContractTemplates;

/// <summary>Lista de placeholders para o painel do editor de modelos. Escopo como string ("Purchase"/"Sales"/"Both").</summary>
public class ContractTemplatesListPlaceholdersController : ODataController
{
    [HttpGet("odata/ContractTemplatesListPlaceholders(ContractType={contractType})")]
    public ActionResult<ICollection<ContractDraftPlaceholderDto>> List([FromRoute] string contractType)
    {
        if (!Enum.TryParse<ContractTemplateScope>(contractType?.Trim('\''), true, out var scope))
            return BadRequest("Tipo de contrato inválido. Use Purchase, Sales ou Both.");

        return Ok(ContractDraftPlaceholderCatalog.For(scope));
    }
}
