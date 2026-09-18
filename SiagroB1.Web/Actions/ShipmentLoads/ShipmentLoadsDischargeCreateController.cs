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

            var loadKey = (Guid) loadKeyObj;
            var userName = User.Identity?.Name ?? "Unknown";

            // Anexo primeiro: o ticket guarda a chave dele, e gravar na ordem inversa deixaria o
            // vínculo para um segundo SaveChanges que pode não acontecer.
            Guid? attachmentKey = null;

            if (fileObj is string base64 && base64.Length > 0)
            {
                var saved = await attachments.SaveAsync(loadKey, new ShipmentLoadAttachment
                {
                    AttachmentType = ShipmentLoadAttachmentType.DischargeTicket,
                    Description = $"Ticket de descarga {ticketObj as string ?? string.Empty}".Trim(),
                    FileName = fileNameObj?.ToString() ?? "ticket",
                    ContentType = contentTypeObj?.ToString() ?? "application/octet-stream",
                    FileData = DecodeFile(base64),
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
                ParseDate(dateObj),
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

    /// <summary>
    /// O arquivo do ticket viaja em base64. Conteúdo corrompido é erro do payload, não falha do
    /// servidor — e a mensagem crua de <c>FormatException</c> vem em inglês.
    /// </summary>
    private static byte[] DecodeFile(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new DefaultException("Não foi possível ler o arquivo do ticket anexado.");
        }
    }

    /// <summary>
    /// A data viaja como string "yyyy-MM-dd". Parâmetro string do EDM é anulável, então o nulo
    /// cai no dia de hoje em vez de estourar.
    /// </summary>
    private static DateTime ParseDate(object? value) =>
        value is string text && DateTime.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : DateTime.Now.Date;
}
