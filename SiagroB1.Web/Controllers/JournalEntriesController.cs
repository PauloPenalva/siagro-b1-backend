using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Lançamentos contábeis: consulta e inclusão manual (cabeçalho + linhas no mesmo POST). Não há PATCH nem DELETE:
/// lançamento gravado só se corrige por estorno (action JournalEntriesReverse). Só STANDALONE.
/// </summary>
public class JournalEntriesController(IJournalEntriesService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [EnableQuery(MaxExpansionDepth = 2)]
    public ActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(service.QueryAll());
    }

    [EnableQuery(MaxExpansionDepth = 2)]
    public ActionResult Get([FromRoute] int key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(SingleResult.Create(service.QueryAll().Where(e => e.DocEntry == key)));
    }

    public async Task<IActionResult> Post([FromBody] JournalEntry entity)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (entity is null) return BadRequest("Informe o lançamento.");

        var draft = new JournalEntryDraft
        {
            PostingDate = entity.PostingDate,
            BranchCode = entity.BranchCode,
            Batch = entity.Batch,
            SubBatch = entity.SubBatch,
            Comments = entity.Comments,
            Lines = entity.Lines.OrderBy(l => l.LineNumber).Select(l => new JournalEntryLineDraft
            {
                EntryType = l.EntryType,
                DebitAccountCode = l.DebitAccountCode,
                CreditAccountCode = l.CreditAccountCode,
                Amount = l.Amount,
                History = l.History,
                DebitCostCenterCode = l.DebitCostCenterCode,
                CreditCostCenterCode = l.CreditCostCenterCode,
                DebitAccountingItemCode = l.DebitAccountingItemCode,
                CreditAccountingItemCode = l.CreditAccountingItemCode,
                BusinessPartnerCode = l.BusinessPartnerCode,
            }).ToList(),
        };

        try
        {
            var created = await service.CreateManualAsync(draft, User.Identity?.Name ?? "Unknown");
            return Created(created);
        }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }
}
