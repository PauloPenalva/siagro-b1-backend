using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>Lê chave de acesso e status da NF-e no banco do add-on InvOne.</summary>
public class InvOneFiscalDocumentSource(InvOneDbContext db) : ISapFiscalDocumentSource
{
    private const int ArInvoiceDocType = 13;
    private const int AuthorizedStatus = 4;
    private const int CancelledStatus = 10;
    private const int BatchSize = 1000;

    public bool IsAvailable => true;

    public async Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
        IReadOnlyCollection<int> docEntries, CancellationToken ct = default)
    {
        var result = new Dictionary<int, SapFiscalDocumentInfo>();

        // Lotes: um IN com milhares de itens estoura o limite de parâmetros do SQL Server.
        foreach (var batch in docEntries.Distinct().Chunk(BatchSize))
        {
            var rows = await db.Processes.AsNoTracking()
                .Where(p => p.DocType == ArInvoiceDocType && batch.Contains(p.DocEntry))
                .ToListAsync(ct);

            foreach (var group in rows.GroupBy(p => p.DocEntry))
            {
                // Reenvios geram mais de uma linha: prefere a autorizada, depois a cancelada.
                var chosen = group.FirstOrDefault(IsAuthorized)
                             ?? group.FirstOrDefault(IsCancelled)
                             ?? group.First();

                result[group.Key] = new SapFiscalDocumentInfo(
                    group.Key,
                    NormalizeKey(chosen.KeyNfe),
                    IsAuthorized(chosen),
                    IsCancelled(chosen),
                    chosen.ProtocolNumber);
            }
        }

        return result;
    }

    private static bool IsAuthorized(InvOneProcess p) => p.StatusId == AuthorizedStatus;

    private static bool IsCancelled(InvOneProcess p) => p.StatusId == CancelledStatus || p.IsCanceled == true;

    private static string? NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 44 ? digits : null;
    }
}
