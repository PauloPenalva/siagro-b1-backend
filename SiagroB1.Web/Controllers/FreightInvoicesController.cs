using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Fatura de frete (só STANDALONE). Leitura pelo entity set (<c>$expand=Items</c>); gravação, emissão, cancelamento e
/// geração em lote pelas ações. Os títulos gerados são lidos em FinancialDocuments com
/// <c>$filter=OriginType eq 'FreightInvoice' and OriginKey eq &lt;guid&gt;</c>.
/// </summary>
public class FreightInvoicesController(
    FreightInvoicesSaveService saveService,
    FreightInvoicesIssueService issueService,
    FreightInvoicesCancelService cancelService,
    FreightInvoicesBatchCreateService batchCreateService,
    FreightInvoiceEligibleCtesQuery eligibleQuery,
    IUnitOfWork db,
    IConfiguration configuration) : ODataController
{
    private const string InvalidKey = "Chave da fatura de frete inválida.";
    private const string InvalidDate = "Data inválida; use o formato aaaa-mm-dd.";

    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        return Ok(db.Context.FreightInvoices.AsNoTracking());
    }

    [EnableQuery(MaxExpansionDepth = 3)]
    public IActionResult Get([FromRoute] Guid key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        return Ok(SingleResult.Create(db.Context.FreightInvoices.AsNoTracking().Where(x => x.Key == key)));
    }

    /// <summary>TakerCardCode, From e To em branco = sem filtro (tomador qualquer / período aberto).</summary>
    [HttpGet("odata/FreightInvoiceEligibleCtes(BranchCode={branchCode},TakerCardCode={takerCardCode},From={from},To={to})")]
    public async Task<IActionResult> EligibleCtesAsync(
        [FromRoute] string? branchCode, [FromRoute] string? takerCardCode, [FromRoute] string? from, [FromRoute] string? to)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        if (string.IsNullOrWhiteSpace(Clean(branchCode))) return BadRequest("Parâmetro obrigatório ausente: BranchCode.");
        if (!TryDate(from, out var fromDate) || !TryDate(to, out var toDate)) return BadRequest(InvalidDate);

        try
        {
            var taker = Clean(takerCardCode);
            return Ok(await eligibleQuery.ListAsync(Clean(branchCode)!, string.IsNullOrEmpty(taker) ? null : taker, fromDate, toDate));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/FreightInvoicesSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        if (parameters?.GetValueOrDefault("Model") is not FreightInvoiceSaveRequest model)
            return BadRequest("Parâmetro obrigatório ausente ou inválido: Model.");

        try
        {
            return Ok(await saveService.SaveAsync(model.ToModel(), UserName));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/FreightInvoicesIssue")]
    public Task<IActionResult> IssueAsync([FromBody] ODataActionParameters? parameters) =>
        RunAsync(parameters, async key =>
        {
            await issueService.IssueAsync(key, UserName);
            return NoContent();
        });

    [HttpPost("odata/FreightInvoicesCancel")]
    public Task<IActionResult> CancelAsync([FromBody] ODataActionParameters? parameters) =>
        RunAsync(parameters, async key =>
        {
            await cancelService.CancelAsync(key, UserName);
            return NoContent();
        });

    [HttpPost("odata/FreightInvoicesBatchCreate")]
    public async Task<IActionResult> BatchCreateAsync([FromBody] ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        var branchCode = Clean(parameters?.GetValueOrDefault("BranchCode")?.ToString());
        if (string.IsNullOrWhiteSpace(branchCode)) return BadRequest("Parâmetro obrigatório ausente: BranchCode.");
        if (!TryDate(parameters!.GetValueOrDefault("From")?.ToString(), out var from) || from is null
            || !TryDate(parameters.GetValueOrDefault("To")?.ToString(), out var to) || to is null)
            return BadRequest("Informe o período (De e Até) no formato aaaa-mm-dd.");

        var taker = Clean(parameters.GetValueOrDefault("TakerCardCode")?.ToString());
        var issue = parameters.GetValueOrDefault("Issue") is true;

        try
        {
            return Ok(await batchCreateService.CreateAsync(branchCode, from.Value, to.Value,
                string.IsNullOrEmpty(taker) ? null : taker, issue, UserName));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private async Task<IActionResult> RunAsync(ODataActionParameters? parameters, Func<Guid, Task<IActionResult>> action)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);
        if (parameters is null || !Guid.TryParse(parameters.GetValueOrDefault("Key")?.ToString(), out var key))
            return BadRequest(InvalidKey);

        try
        {
            return await action(key);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Texto de rota OData chega entre aspas simples e, em parâmetro opcional, pode vir nulo.</summary>
    private static string? Clean(string? value) => value?.Trim().Trim('\'').Trim();

    private static bool TryDate(string? value, out DateTime? date)
    {
        date = null;
        var text = Clean(value);
        if (string.IsNullOrEmpty(text) || text == "null") return true;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        date = parsed.Date;
        return true;
    }

    private string UserName => User.Identity?.Name ?? "Unknown";
}
