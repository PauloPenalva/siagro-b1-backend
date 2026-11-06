using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>
/// Escopo (por requisição) em que o próprio EFP chama os serviços de fixação: dentro dele o
/// <see cref="MarketEfpFixationGuard"/> não recusa, para o EFP conseguir aprovar, estornar, excluir e rejeitar a fixação
/// que ele mesmo gerou.
/// </summary>
public sealed class MarketEfpFixationScope
{
    private int depth;

    public bool IsActive => depth > 0;

    public IDisposable Enter()
    {
        depth++;
        return new Exit(this);
    }

    private sealed class Exit(MarketEfpFixationScope scope) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            scope.depth--;
        }
    }
}

/// <summary>
/// Trava das ações manuais de fixação (alterar, excluir, rejeitar, estornar): a fixação gerada por um EFP não cancelado
/// só se mexe pelo EFP. Sem linha em MARKET_EFPS para a chave (clientes sem o módulo), nada muda. Uma consulta por
/// chamada, pelo índice de <c>FixationKey</c>.
/// </summary>
public class MarketEfpFixationGuard(AppDbContext context, MarketEfpFixationScope scope)
{
    public async Task EnsureNotEfpFixationAsync(Guid fixationKey)
    {
        if (scope.IsActive)
            return;
        var efpId = await context.MarketEfps.AsNoTracking()
            .Where(e => e.FixationKey == fixationKey && e.Status != MarketEfpStatus.Canceled)
            .Select(e => (int?)e.Id).FirstOrDefaultAsync();
        if (efpId is not null)
            throw new ApplicationException(
                $"Esta fixação foi gerada pelo EFP nº {efpId}. Estorne pelo EFP (Mercado → EFP).");
    }
}
