using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Leitura do documento de entrada.
///
/// O <c>ThenInclude(SalesInvoiceItem)</c> NÃO é decorativo: a quebra apurada e a diferença da linha
/// são calculadas a partir da linha de origem, e sem ele voltam ZERO EM SILÊNCIO — toda linha de
/// devolução pareceria divergente.
///
/// O nível seguinte, <c>SalesInvoice</c>, é o que dá o NÚMERO da nota de origem exibido na grade.
/// Sem ele o <c>$expand</c> do cliente não adianta nada: o OData serializa o grafo que o EF
/// materializou, então navegação sem Include volta null e a coluna fica vazia mesmo com a
/// amarração gravada.
///
/// Comentários e log de alterações NÃO entram aqui de propósito: são coleções que só a tela de
/// detalhe consome, e carregá-las em toda listagem sairia caro. O frontend as busca em binding
/// próprio, com <c>$$ownRequest</c>.
/// </summary>
public class PurchaseInvoicesGetService(IUnitOfWork db)
{
    public IQueryable<PurchaseInvoice> QueryAll()
    {
        return db.Context.PurchaseInvoices
            .Include(x => x.Items)
            .ThenInclude(i => i.SalesInvoiceItem)
            .ThenInclude(si => si!.SalesInvoice)
            .AsNoTracking();
    }

    /// <summary>
    /// SEM rastreamento, e isso é essencial e não otimização.
    ///
    /// O PATCH do OData chama este método, aplica o <c>Delta</c> sobre o que voltou e entrega o
    /// resultado ao <c>PurchaseInvoicesUpdateService</c>. Se a entidade viesse rastreada, o serviço
    /// carregaria O MESMO objeto como "existente" — comparar o entrante com o existente seria
    /// comparar o objeto consigo mesmo, e a reconciliação das linhas viraria no-op silencioso.
    /// </summary>
    public async Task<PurchaseInvoice> GetByIdAsync(Guid key)
    {
        return await db.Context.PurchaseInvoices
                   .Include(x => x.Items)
                   .ThenInclude(i => i.SalesInvoiceItem)
                   .ThenInclude(si => si!.SalesInvoice)
                   .AsNoTracking()
                   .FirstOrDefaultAsync(x => x.Key == key)
               ?? throw new NotFoundException("Documento de entrada não encontrado.");
    }
}
