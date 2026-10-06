using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Grava as CC-e registradas na SEFAZ que ainda não estão no documento (resposta perdida, carta emitida fora do
/// Siagro). Idempotente: sequência já gravada é ignorada. Não salva — quem chama decide o SaveChanges.
/// </summary>
public static class NfeCorrectionImporter
{
    /// <summary>Autor gravado nas cartas que vieram da consulta.</summary>
    public const string ConsultUser = "Consulta SEFAZ";

    public static async Task<int> ImportAsync<TDocument>(
        INfeDocumentStore<TDocument> store, TDocument document, IReadOnlyList<NfeEventResult>? corrections, string userName)
        where TDocument : class, INfeDocument
    {
        if (corrections is null || corrections.Count == 0)
            return 0;

        var existing = (await store.CorrectionSequencesAsync(document.Key)).ToHashSet();
        var imported = 0;

        foreach (var correction in corrections.Where(c => c.Sequence is not null).OrderBy(c => c.Sequence))
        {
            if (!existing.Add(correction.Sequence!.Value))
                continue;

            store.AddCorrection(document, correction.Sequence.Value, correction.CorrectionText ?? string.Empty, correction, userName);
            imported++;
        }

        return imported;
    }
}
