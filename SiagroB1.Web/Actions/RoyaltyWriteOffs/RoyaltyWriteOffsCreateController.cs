using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.RoyaltyWriteOffs;

public class RoyaltyWriteOffsCreateController(RoyaltyWriteOffsCreateService service) : ODataController
{
    /// <remarks>
    /// <paramref name="parameters"/> vem NULO sem nenhum parâmetro do EDM, e opcionais chegam nulos:
    /// nada de <c>.ToString()</c> direto. Pesos chegam como double (o UI5 manda Edm.Decimal como string).
    /// </remarks>
    [HttpPost("odata/RoyaltyWriteOffsCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("WriteOffDate", out var dateObj) ||
                !DateTime.TryParseExact(dateObj?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var writeOffDate))
                return BadRequest("Informe a data da baixa.");

            parameters.TryGetValue("ItsId", out var itsIdObj);
            parameters.TryGetValue("TaxId", out var taxIdObj);
            parameters.TryGetValue("Name", out var nameObj);
            parameters.TryGetValue("Comments", out var commentsObj);
            parameters.TryGetValue("StorageTransactionKeys", out var keysObj);
            parameters.TryGetValue("Weights", out var weightsObj);

            var keys = (keysObj as IEnumerable<Guid>)?.ToList() ?? [];
            var weights = new List<double>();
            if (weightsObj is IEnumerable sequence)
                foreach (var item in sequence)
                    weights.Add(item switch
                    {
                        double d => d,
                        decimal m => (double)m,
                        int i => i,
                        long l => l,
                        float f => f,
                        _ => double.TryParse(item?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                            ? parsed
                            : throw new ApplicationException($"Peso inválido: {item}"),
                    });
            if (keys.Count != weights.Count)
                return BadRequest("Romaneios e pesos não conferem.");

            var input = new RoyaltyWriteOffInput(
                writeOffDate,
                itsIdObj?.ToString() ?? "",
                taxIdObj?.ToString() ?? "",
                nameObj?.ToString() ?? "",
                commentsObj?.ToString(),
                keys.Select((k, i) => new RoyaltyWriteOffLineInput(k, Math.Round((decimal)weights[i], 3))).ToList());

            var userName = User.Identity?.Name ?? "Unknown";
            var writeOff = await service.ExecuteAsync(input, userName);
            return Ok(new { writeOff.Key, writeOff.RowId });
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);
            if (e is ApplicationException)
                return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
