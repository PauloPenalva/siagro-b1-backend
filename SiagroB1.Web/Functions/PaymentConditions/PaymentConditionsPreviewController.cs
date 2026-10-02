using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Infra;

namespace SiagroB1.Web.Functions.PaymentConditions;

/// <summary>
/// Prévia das parcelas para a tela, com o MESMO cálculo da emissão — a condição ainda não
/// precisa estar gravada (os parâmetros são os do formulário).
/// </summary>
public class PaymentConditionsPreviewController(IConfiguration configuration) : ODataController
{
    [HttpGet("odata/PaymentConditionsPreview(Days={days},StartRule={startRule},PaymentMeans={paymentMeans},Total={total},IssueDate={issueDate})")]
    public IActionResult Get(
        [FromRoute] string days, [FromRoute] int startRule, [FromRoute] string paymentMeans,
        [FromRoute] decimal total, [FromRoute] DateOnly issueDate)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest("As condições de pagamento só existem no modo STANDALONE.");

        try
        {
            // Rotas por atributo entregam o segmento COM as aspas simples do OData.
            var plan = PaymentInstallmentCalculator.Calculate(
                days.Trim('\''), (PaymentStartRule)startRule, paymentMeans.Trim('\''), total, issueDate);

            return Ok(plan.Installments.Select(i => new PaymentInstallmentPreviewDto
            {
                Number = i.Number, DueDate = i.DueDate, Amount = i.Amount,
            }));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
