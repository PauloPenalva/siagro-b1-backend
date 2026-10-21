using SiagroB1.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialAdvancesCreateController(FinancialAdvancesCreateService service) : ODataController
{
    [HttpPost("odata/FinancialAdvancesCreate")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            parameters.TryGetValue("ContractKey", out var contractObj);
            var withContract = contractObj is not null && !string.IsNullOrWhiteSpace(contractObj.ToString());

            object? typeObj = null;
            if (withContract && (!parameters.TryGetValue("ContractType", out typeObj) || typeObj is null))
                return BadRequest("Informe o tipo de contrato.");

            FinancialDirection direction = default;
            if (!withContract)
            {
                parameters.TryGetValue("Direction", out var directionObj);
                var directionText = directionObj?.ToString();
                if (string.Equals(directionText, "Payable", StringComparison.OrdinalIgnoreCase))
                    direction = FinancialDirection.Payable;
                else if (string.Equals(directionText, "Receivable", StringComparison.OrdinalIgnoreCase))
                    direction = FinancialDirection.Receivable;
                else
                    return BadRequest("Informe a direção do adiantamento (a pagar ou a receber).");
            }

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor do adiantamento.");

            if (!parameters.TryGetValue("DueDate", out var dueObj) || dueObj is null)
                return BadRequest("Informe o vencimento do adiantamento.");

            if (!parameters.TryGetValue("FinancialAccountCode", out var accountObj) || accountObj is null ||
                string.IsNullOrWhiteSpace(accountObj.ToString()))
                return BadRequest("Informe a conta financeira do pagamento do adiantamento.");

            if (!parameters.TryGetValue("PaymentDate", out var paymentObj) || paymentObj is null)
                return BadRequest("Informe a data do pagamento do adiantamento.");

            parameters.TryGetValue("Comments", out var commentsObj);

            if (!withContract)
            {
                parameters.TryGetValue("CardCode", out var cardObj);
                parameters.TryGetValue("BranchCode", out var branchObj);

                var free = await service.ExecuteWithoutContractAsync(
                    direction: direction,
                    cardCode: cardObj?.ToString() ?? string.Empty,
                    branchCode: branchObj?.ToString() ?? string.Empty,
                    amount: Convert.ToDecimal(amountObj),
                    dueDate: DateTime.Parse(dueObj.ToString()!),
                    financialAccountCode: accountObj.ToString()!,
                    paymentDate: DateTime.Parse(paymentObj.ToString()!),
                    comments: commentsObj?.ToString(),
                    userName: User.Identity?.Name ?? "Unknown",
                    dimensions: Dimensions(parameters));

                return Ok(new { free.Key, free.Code });
            }

            var advance = await service.ExecuteAsync(
                contractType: typeObj!.ToString()!,
                contractKey: Guid.Parse(contractObj!.ToString()!),
                amount: Convert.ToDecimal(amountObj),
                dueDate: DateTime.Parse(dueObj.ToString()!),
                financialAccountCode: accountObj.ToString()!,
                paymentDate: DateTime.Parse(paymentObj.ToString()!),
                comments: commentsObj?.ToString(),
                userName: User.Identity?.Name ?? "Unknown",
                dimensions: Dimensions(parameters));

            return Ok(new { advance.Key, advance.Code });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }

    private static AccountingAssignmentSnapshot Dimensions(ODataActionParameters p)
    {
        string? Text(string name) => p.TryGetValue(name, out var v) ? v?.ToString() : null;
        return new AccountingAssignmentSnapshot
        {
            CostCenterCode = Text("CostCenterCode"),
            AccountingItemCode = Text("AccountingItemCode"),
            DistributionRuleCode = Text("DistributionRuleCode"),
            LedgerAccountCode = Text("LedgerAccountCode"),
        };
    }
}
