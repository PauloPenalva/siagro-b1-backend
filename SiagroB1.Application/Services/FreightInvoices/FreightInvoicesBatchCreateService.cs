using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>Resultado da geração em lote: faturas criadas e erros por tomador ("{Tomador}: {mensagem}").</summary>
public record FreightInvoiceBatchResult(IReadOnlyList<Guid> Created, IReadOnlyList<string> Errors);

/// <summary>
/// Gera um rascunho de fatura de frete por tomador com os CT-e elegíveis do período (numeração Automática, emissão da
/// fatura = fim do período). Com <c>issue</c>, emite cada rascunho; se a emissão falhar, o rascunho (válido) é mantido e o
/// erro é reportado. A falha de um tomador não aborta os demais. Só STANDALONE.
/// </summary>
public class FreightInvoicesBatchCreateService(
    IUnitOfWork db,
    FreightInvoiceEligibleCtesQuery eligible,
    FreightInvoicesSaveService save,
    FreightInvoicesIssueService issueService,
    IConfiguration configuration,
    ILogger<FreightInvoicesBatchCreateService> logger)
{
    public async Task<FreightInvoiceBatchResult> CreateAsync(
        string branchCode, DateTime from, DateTime to, string? takerCardCode, bool issue, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);

        var rows = await eligible.ListAsync(branchCode,
            string.IsNullOrWhiteSpace(takerCardCode) ? null : takerCardCode, from, to);
        var created = new List<Guid>();
        var errors = new List<string>();

        foreach (var group in rows.Where(r => r.TakerCardCode != null).GroupBy(r => r.TakerCardCode!).OrderBy(g => g.Key))
        {
            Guid? key = null;
            try
            {
                var model = new FreightInvoiceSaveModel(null, branchCode, group.Key, to.Date, null, null, null,
                    group.Select(r => r.TransportDocumentKey).ToList(), null, null);
                var invoice = await save.SaveAsync(model, user);
                key = invoice.Key;
                created.Add(key.Value);
                if (issue) await issueService.IssueAsync(key.Value, user);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (key is null) db.Context.ChangeTracker.Clear(); // nada fica pela metade
                if (ex is DefaultException) errors.Add($"{group.Key}: {ex.Message}");
                else
                {
                    logger.LogError(ex, "Falha inesperada ao gerar a fatura de frete do tomador {Taker}.", group.Key);
                    errors.Add($"{group.Key}: erro inesperado ao gerar a fatura.");
                }
            }
        }
        return new FreightInvoiceBatchResult(created, errors);
    }
}
