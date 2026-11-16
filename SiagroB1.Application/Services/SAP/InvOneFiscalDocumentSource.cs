using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>Lê chave de acesso e status da NF-e no banco do add-on InvOne.</summary>
/// <remarks>
/// Como a linha AUTORIZADA vence o desempate, um cancelamento na SEFAZ gravado pelo add-on como NOVA linha de
/// <c>Process</c> ao lado da autorizada original não é visto por aqui — para esse caso a sincronização conta com
/// <c>OINV.CANCELED = 'Y'</c> no SAP.
/// </remarks>
public class InvOneFiscalDocumentSource(InvOneDbContext db) : ISapFiscalDocumentSource
{
    private const int ArInvoiceDocType = 13;
    private const int AuthorizedStatus = 4;
    private const int CancelledStatus = 10;
    private const int BatchSize = 1000;

    public bool IsAvailable => true;

    /// <remarks>
    /// O resultado é indexado por DocEntry: se o mesmo DocEntry for pedido para duas filiais, as
    /// linhas das duas filiais concorrem e vale a melhor delas pela ordem abaixo (autorizada primeiro). Quem chama não deve misturar filiais com o mesmo DocEntry (no SAP o
    /// DocEntry é único por base).
    /// </remarks>
    public async Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
        IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default)
    {
        var result = new Dictionary<int, SapFiscalDocumentInfo>();

        // Sem filial não há como saber de qual base é a linha do add-on: não casa com nada.
        var wanted = invoices.Where(i => i.BranchId.HasValue)
            .Select(i => (i.DocEntry, Branch: i.BranchId!.Value))
            .Distinct()
            .ToList();
        var branches = wanted.Select(w => w.Branch).Distinct().ToList();
        var pairs = wanted.ToHashSet();

        // Lotes: um IN com milhares de itens estoura o limite de parâmetros do SQL Server.
        foreach (var batch in wanted.Select(w => w.DocEntry).Distinct().Chunk(BatchSize))
        {
            // A tabela guarda várias bases (CompanyId = OINV.BPLId): filtra por filial também.
            var rows = await BuildQuery(db, batch, branches).ToListAsync(ct);

            foreach (var group in rows.Where(p => p.DocEntry.HasValue && p.CompanyId.HasValue
                                             && pairs.Contains((p.DocEntry.Value, p.CompanyId.Value)))
                         .GroupBy(p => p.DocEntry!.Value))
            {
                // Reenvios geram várias linhas. Ordem explícita (o SQL não garante nenhuma): autorizada,
                // depois cancelada, depois as demais; empate resolvido por protocolo e chave.
                var chosen = group
                    .OrderByDescending(IsAuthorized)
                    .ThenByDescending(IsCancelled)
                    .ThenByDescending(p => p.ProtocolNumber, StringComparer.Ordinal)
                    .ThenByDescending(p => p.KeyNfe, StringComparer.Ordinal)
                    .First();

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

    /// <summary>Consulta de um lote; separada para o teste provar que traduz para SQL.</summary>
    internal static IQueryable<InvOneProcess> BuildQuery(
        InvOneDbContext db, IReadOnlyCollection<int> docEntries, IReadOnlyCollection<int> branches)
    {
        var entries = docEntries.Select(d => (int?)d).ToList();
        var branchList = branches.Select(b => (int?)b).ToList();
        return db.Processes.AsNoTracking()
            .Where(p => p.DocType == ArInvoiceDocType
                        && entries.Contains(p.DocEntry)
                        && branchList.Contains(p.CompanyId));
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
