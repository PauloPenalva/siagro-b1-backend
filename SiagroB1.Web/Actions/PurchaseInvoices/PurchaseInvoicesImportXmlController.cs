using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseInvoices;

/// <summary>
/// Lê o XML da NF-e de entrada e devolve o rascunho preenchido.
///
/// Action OData, e não upload REST em <c>/api</c>: o dev server e o Gateway só encaminham
/// <c>/odata</c>, <c>/security</c> e <c>/reports</c> — um endpoint em <c>/api</c> simplesmente não
/// chega ao backend pelo navegador. O XML é texto, então vai como parâmetro da action, sem
/// multipart.
///
/// Só LÊ: não grava nada. Quem persiste é o POST da entidade, depois de o operador conferir e, no
/// caso da devolução, amarrar as linhas com as notas de origem.
/// </summary>
public class PurchaseInvoicesImportXmlController(PurchaseInvoicesImportXmlService service)
    : ODataController
{
    [HttpPost("odata/PurchaseInvoicesImportXml")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // Parâmetro de string do OData é ANULÁVEL: TryGetValue devolve true com null, e um
            // .ToString() direto estoura em NullReferenceException.
            if (!parameters.TryGetValue("XmlContent", out var contentObj) ||
                contentObj?.ToString() is not { Length: > 0 } xmlContent)
            {
                return BadRequest("Conteúdo do XML não informado.");
            }

            parameters.TryGetValue("FileName", out var fileNameObj);

            var draft = await service.ExecuteAsync(
                System.Text.Encoding.UTF8.GetBytes(xmlContent),
                fileNameObj?.ToString() ?? "nfe.xml");

            return Ok(draft);
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
