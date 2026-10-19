using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseTransactionsChangeSupplierController(
    PurchaseTransactionsChangeSupplierService changeSupplierService
    ) : ODataController
{
    private const int MaxReasonLength = 500;

    [HttpPost("odata/PurchaseTransactionsChangeSupplier")]
    public async Task<IActionResult> ChangeSupplier(ODataActionParameters parameters)
    {
        try
        {
            // Parâmetro ausente do corpo deixa `parameters` NULO; string presente pode vir null.
            if (parameters == null)
                return BadRequest("Parâmetros obrigatórios não informados.");

            if (!parameters.TryGetValue("StorageTransactionKey", out var transactionObj) ||
                transactionObj is not Guid transactionKey ||
                !parameters.TryGetValue("PurchaseContractKey", out var contractObj) ||
                contractObj is not Guid contractKey ||
                !parameters.TryGetValue("Volume", out var volumeObj) ||
                volumeObj is not double volumeValue)
            {
                return BadRequest("Parâmetros obrigatórios não informados.");
            }

            parameters.TryGetValue("Reason", out var reasonObj);
            var reason = reasonObj as string;

            // Motivo vazio é recusado pelo serviço; o limite de tamanho é validado aqui.
            if (reason != null && reason.Trim().Length > MaxReasonLength)
                return BadRequest("O motivo pode ter no máximo 500 caracteres.");

            // Volume trafega como double (decimal editável chega como string e é recusado).
            var volume = Convert.ToDecimal(volumeValue);
            var userName = User.Identity?.Name ?? "Unknown";

            var result = await changeSupplierService.ExecuteAsync(
                transactionKey, contractKey, volume, reason, userName);

            return Ok(new PurchaseTransactionChangeSupplierResponse(result.ReturnCode, result.NewCode));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            // A exceção pode chegar direta ou embrulhada (ex.: rollback).
            if (e is DbUpdateConcurrencyException || e.InnerException is DbUpdateConcurrencyException)
            {
                return BadRequest(
                    "O romaneio foi alterado por outro usuário. Recarregue a tela e tente novamente.");
            }

            return BadRequest(e.Message);
        }
    }
}

// [JsonPropertyName] mantém PascalCase: sem ele a API serializa a resposta em camelCase.
public sealed record PurchaseTransactionChangeSupplierResponse(
    [property: JsonPropertyName("ReturnCode")] string ReturnCode,
    [property: JsonPropertyName("NewCode")] string NewCode);
