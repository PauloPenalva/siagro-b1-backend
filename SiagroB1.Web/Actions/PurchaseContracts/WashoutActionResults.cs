using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

/// <summary>
/// Mapeamento de erro das actions de washout. Regra de negócio é 400/404 com um payload de erro
/// OData de verdade (<see cref="BadRequestODataResult"/>/<see cref="NotFoundODataResult"/>, NÃO
/// <c>ControllerBase.BadRequest(string)</c>/<c>NotFound(string)</c>, que devolvem a mensagem
/// como um objeto qualquer e o formatter serializa como <c>Edm.String</c> cru) — sem isso o
/// modelo OData v4 do UI5 não consegue extrair a mensagem de negócio e mostra apenas
/// "Communication error: 400 Bad Request". Não previsto continua 500.
/// </summary>
internal static class WashoutActionResults
{
    public static IActionResult FromException(ControllerBase controller, Exception e) => e switch
    {
        NotFoundException or KeyNotFoundException => new NotFoundODataResult(e.Message),
        // Precisa vir ANTES do DbUpdateException genérico: DbUpdateConcurrencyException deriva dele.
        DbUpdateConcurrencyException => new BadRequestODataResult(
            "Os dados foram alterados por outra operação. Recarregue a tela e tente de novo."),
        // F5 (revisão final): violação de constraint (índice único filtrado, FK) chegava como
        // DbUpdateException não tratada e virava 500 — sem mensagem de negócio pra tela mostrar.
        DbUpdateException => new BadRequestODataResult(
            "Não foi possível gravar o washout: outra operação alterou os mesmos dados. " +
            "Recarregue a tela e tente de novo."),
        DefaultException or BusinessException or ApplicationException => new BadRequestODataResult(e.Message),
        _ => controller.StatusCode(500, e.Message),
    };
}
