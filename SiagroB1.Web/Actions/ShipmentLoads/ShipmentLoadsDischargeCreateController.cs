using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Registra um ticket de descarga na carga (GAC-1171), opcionalmente já com o arquivo do ticket
/// anexado no mesmo diálogo.
/// </summary>
public class ShipmentLoadsDischargeCreateController(
    ShipmentLoadDischargesCreateService service,
    ShipmentLoadAttachmentsCreateService attachments) : ODataController
{
    [HttpPost("odata/ShipmentLoadsDischargeCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado, e o TryGetValue
            // de um parâmetro anulável devolve true com valor nulo — daí as duas checagens.
            if (parameters is null ||
                !parameters.TryGetValue("LoadKey", out var loadKeyObj) || loadKeyObj is null ||
                !parameters.TryGetValue("SalesInvoiceKey", out var invoiceKeyObj) || invoiceKeyObj is null ||
                !parameters.TryGetValue("SalesInvoiceItemKey", out var itemKeyObj) || itemKeyObj is null)
                return BadRequest("Carga, documento de saída e item são obrigatórios.");

            parameters.TryGetValue("TicketNumber", out var ticketObj);
            parameters.TryGetValue("DischargeDate", out var dateObj);
            parameters.TryGetValue("Quantity", out var quantityObj);
            parameters.TryGetValue("Comments", out var commentsObj);
            parameters.TryGetValue("File", out var fileObj);
            parameters.TryGetValue("FileName", out var fileNameObj);
            parameters.TryGetValue("ContentType", out var contentTypeObj);

            // Payload validado ANTES de gravar o anexo: o anexo tem SaveChanges próprio, então
            // recusar o ticket depois dele deixaria um arquivo sem ticket na aba de anexos à toa.
            if (!ShipmentLoadActionParameters.TryParseDate(dateObj, out var parsedDate))
                return BadRequest(ShipmentLoadActionParameters.InvalidDateMessage);

            // Só aqui a ausência cai no dia de hoje: registrar sem informar data significa "hoje".
            // Na alteração isso seria destrutivo — ver ShipmentLoadsDischargeUpdateController.
            var dischargeDate = parsedDate ?? DateTime.Now.Date;

            var loadKey = (Guid) loadKeyObj;
            var userName = User.Identity?.Name ?? "Unknown";

            // Anexo primeiro: o ticket guarda a chave dele, e gravar na ordem inversa deixaria o
            // vínculo para um segundo SaveChanges que pode não acontecer.
            Guid? attachmentKey = null;

            if (fileObj is string base64 && base64.Length > 0)
            {
                if (!ShipmentLoadActionParameters.TryDecodeFile(base64, out var fileData))
                    return BadRequest(ShipmentLoadActionParameters.UnreadableFileMessage);

                var saved = await attachments.SaveAsync(loadKey, new ShipmentLoadAttachment
                {
                    AttachmentType = ShipmentLoadAttachmentType.DischargeTicket,
                    Description = $"Ticket de descarga {ticketObj as string ?? string.Empty}".Trim(),
                    FileName = fileNameObj?.ToString() ?? "ticket",
                    ContentType = contentTypeObj?.ToString() ?? "application/octet-stream",
                    FileData = fileData,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userName,
                });

                attachmentKey = saved.Key;
            }

            await service.ExecuteAsync(
                loadKey,
                (Guid) invoiceKeyObj,
                (Guid) itemKeyObj,
                ticketObj as string,
                dischargeDate,
                Convert.ToDecimal(quantityObj ?? 0d, CultureInfo.InvariantCulture),
                commentsObj as string,
                attachmentKey,
                userName);

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
