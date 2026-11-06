using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Application.Services.Market;

/// <summary>
/// Inclusão dos cadastros do Mercado. O <c>BaseService.CreateAsync</c> genérico troca qualquer falha por
/// "Error creating entity." e esconde a causa; aqui a <see cref="DbUpdateException"/> sobe intacta para o controller
/// traduzir em 400 pt-BR (duplicidade, valor fora do limite).
/// </summary>
internal static class MarketPersistence
{
    public static async Task<T> AddAndSaveAsync<T>(DbContext context, T entity) where T : class
    {
        try
        {
            context.Add(entity);
            await context.SaveChangesAsync();
            return entity;
        }
        catch (DbUpdateException)
        {
            // O que ficou rastreado como Added não pode ir num SaveChanges futuro do mesmo escopo.
            context.ChangeTracker.Clear();
            throw;
        }
    }
}
