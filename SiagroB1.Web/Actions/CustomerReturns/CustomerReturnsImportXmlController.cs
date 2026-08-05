using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.CustomerReturns;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.CustomerReturns;

/// <summary>
/// Lê o XML da NF-e de devolução e devolve o rascunho preenchido.
///
/// Action OData, e não upload REST em <c>/api</c>: o dev server e o Gateway só encaminham
/// <c>/odata</c>, <c>/security</c> e <c>/reports</c> — um endpoint em <c>/api</c> simplesmente
/// não chega ao backend pelo navegador. O XML é texto, então vai como parâmetro da action, sem
/// multipart.
///
/// Só LÊ: não grava nada. Quem persiste é o POST da entidade, depois de o operador amarrar as
/// linhas com as notas de origem.
/// </summary>
public class CustomerReturnsImportXmlController(CustomerReturnsImportXmlService service)
    : ODataController
{
    [HttpPost("odata/CustomerReturnsImportXml")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // Parâmetro de string do OData é anulável: TryGetValue devolve true com null.
            if (!parameters.TryGetValue("XmlContent", out var contentObj) ||
                contentObj?.ToString() is not { Length: > 0 } xmlContent)
            {
                return BadRequest("Conteúdo do XML não informado.");
            }

            parameters.TryGetValue("FileName", out var fileNameObj);

            var draft = await service.ExecuteAsync(
                System.Text.Encoding.UTF8.GetBytes(xmlContent),
                fileNameObj?.ToString() ?? "devolucao.xml");

            return Ok(new CustomerReturnDraftDto
            {
                CardCode = draft.CardCode,
                CardName = draft.CardName,
                DocumentNumber = draft.DocumentNumber,
                DocumentSeries = draft.DocumentSeries,
                AccessKey = draft.AccessKey,
                IssueDate = draft.IssueDate,
                TotalValue = draft.TotalValue,
                TaxPayerComments = draft.TaxPayerComments,
                XmlFileName = draft.XmlFileName,
                Items = draft.Items.Select(i => new CustomerReturnDraftItemDto
                {
                    ItemCode = i.ItemCode,
                    ItemName = i.ItemName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                }).ToList(),
            });
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
