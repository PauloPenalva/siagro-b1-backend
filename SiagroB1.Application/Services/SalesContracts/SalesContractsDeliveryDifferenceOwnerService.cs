using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Mantém a invariante "um dono da diferença de entrega por item de nota": exatamente UMA
/// linha do ledger carrega a quebra apurada na conferência, em vez de ela ser rateada
/// pró-rata entre os contratos que dividem o item.
///
/// Regra de designação, em três passos:
/// <list type="number">
/// <item>o dono atual permanece, se o contrato dele ainda tem volume líquido positivo no item;</item>
/// <item>senão, assume a linha mais antiga entre as que estão em contrato com líquido positivo
/// — é o que faz a titularidade ACOMPANHAR O VOLUME quando uma realocação zera a origem;</item>
/// <item>se nenhum contrato tem líquido positivo (item integralmente devolvido), permanece a
/// linha mais antiga do item, para o item nunca ficar sem dono.</item>
/// </list>
///
/// É idempotente e auto-corretiva: rodar de novo sobre o mesmo conjunto não muda nada, e um
/// estado inconsistente (nenhum dono, ou mais de um) converge para exatamente um. Por isso o
/// estorno de realocação não precisa de bookkeeping — apagado o par, a regra reelege sozinha
/// a linha do faturamento.
/// </summary>
public static class SalesContractsDeliveryDifferenceOwnerService
{
    /// <summary>
    /// Aplica a regra sobre o conjunto PÓS-MUTAÇÃO das linhas de um item (persistidas +
    /// pendentes − excluídas). Não persiste: quem chama é dono do SaveChanges.
    /// </summary>
    public static void EnsureOwner(IReadOnlyCollection<SalesContractAllocation> linesOfItem)
    {
        if (linesOfItem.Count == 0)
            return;

        // Linha pendente ainda não tem RowId (identity só é atribuído no SaveChanges).
        // Ordenar cru a tornaria a MAIS ANTIGA e roubaria a titularidade da linha do
        // faturamento — por isso RowId 0 vai para o fim.
        var byAge = linesOfItem
            .OrderBy(l => l.RowId == 0 ? int.MaxValue : l.RowId)
            .ToList();

        var netByContract = byAge
            .GroupBy(l => l.SalesContractKey)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Volume));

        bool HasVolume(SalesContractAllocation line) =>
            netByContract.TryGetValue(line.SalesContractKey, out var net) && net > 0;

        // FirstOrDefault, não SingleOrDefault: estado com dois donos é justamente o que este
        // serviço existe para consertar, não para estourar.
        var current = byAge.FirstOrDefault(l => l.OwnsDeliveryDifference);

        var owner = current is not null && HasVolume(current)
            ? current
            : byAge.FirstOrDefault(HasVolume) ?? byAge[0];

        foreach (var line in byAge)
            line.OwnsDeliveryDifference = ReferenceEquals(line, owner);
    }

    /// <summary>
    /// Carrega as linhas dos itens informados (com o item incluído) e aplica
    /// <see cref="EnsureOwner"/> a cada um. Caminho dos hooks que não têm linhas pendentes em
    /// mãos (fechamento de entrega, devolução, recálculo em lote).
    ///
    /// Devolve as linhas RASTREADAS já com a titularidade corrigida — o chamador precisa
    /// delas para somar em memória, porque a flag nova ainda não está no banco.
    /// </summary>
    public static async Task<List<SalesContractAllocation>> EnsureOwnerAsync(
        AppDbContext context, ICollection<Guid> itemKeys)
    {
        if (itemKeys.Count == 0)
            return [];

        var lines = await context.SalesContractsAllocations
            .Include(a => a.SalesInvoiceItem)
            .Where(a => itemKeys.Contains(a.SalesInvoiceItemKey))
            .ToListAsync();

        foreach (var group in lines.GroupBy(a => a.SalesInvoiceItemKey))
            EnsureOwner(group.ToList());

        return lines;
    }
}
