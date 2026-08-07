using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Atualiza o documento de entrada — cabeçalho E LINHAS.
///
/// As linhas são o ponto. A AMARRAÇÃO de cada linha com a nota de origem é manual e normalmente
/// feita DEPOIS de importar o XML, que não carrega esse vínculo. O serviço equivalente da devolução
/// antiga (<c>CustomerReturnsUpdateService</c>) nunca tocava a coleção, e com o Detail read-only
/// não existia caminho nenhum para amarrar depois de gravar.
///
/// Só documento PENDENTE é alterável: confirmado tem efeito de negócio pendurado (Fase 3) e precisa
/// passar pelo estorno.
/// </summary>
public class PurchaseInvoicesUpdateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService)
{
    public async Task ExecuteAsync(Guid key, PurchaseInvoice entity, string userName)
    {
        var existing = await db.Context.PurchaseInvoices
                           .Include(x => x.Items)
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (existing.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ser alterado. Estorne a confirmação antes.");

        existing.InvoiceType = entity.InvoiceType;
        existing.IssuerType = entity.IssuerType;

        // O emitente é editável enquanto o documento está pendente. Sem estas linhas a troca era
        // descartada em silêncio — a tela gravava sem erro e voltava com o emitente antigo.
        //
        // O NOME é re-resolvido quando o código muda, e não copiado: o value help grava a
        // descrição com group ID null de propósito, então o PATCH chega com o nome do emitente
        // ANTERIOR. Copiar direto deixava o documento com o código de um parceiro e o nome de
        // outro — e é o nome que a lista e os relatórios mostram.
        var issuerChanged = existing.CardCode != entity.CardCode;

        existing.CardCode = entity.CardCode;
        existing.CardName = entity.CardName;

        if (issuerChanged || string.IsNullOrWhiteSpace(existing.CardName))
            existing.CardName =
                (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName
                ?? entity.CardName;
        existing.InvoiceNumber = entity.InvoiceNumber;
        existing.TaxDocumentNumber = entity.TaxDocumentNumber;
        existing.TaxDocumentSeries = entity.TaxDocumentSeries;
        existing.ChaveNFe = entity.ChaveNFe;
        existing.IssueDate = entity.IssueDate;
        existing.PostingDate = entity.PostingDate;
        existing.TotalDocumentValue = entity.TotalDocumentValue;
        existing.TaxPayerComments = entity.TaxPayerComments;
        existing.Comments = entity.Comments;
        existing.GrossWeight = entity.GrossWeight;
        existing.NetWeight = entity.NetWeight;
        existing.TruckCode = entity.TruckCode;
        existing.TruckingCompanyCode = entity.TruckingCompanyCode;
        existing.TruckingCompanyName = entity.TruckingCompanyName;
        existing.FreightTerms = entity.FreightTerms;
        existing.PurchaseInvoiceOriginKey = entity.PurchaseInvoiceOriginKey;
        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = userName;

        await SyncItemsAsync(existing, entity);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reconcilia a coleção pela chave da linha: casa atualiza, sobra no entrante insere, sobra no
    /// existente remove.
    ///
    /// Reconciliar em vez de limpar-e-recriar preserva o <c>RowId</c> e a identidade das linhas que
    /// só mudaram de amarração — que é o caso mais comum aqui.
    ///
    /// A remoção é feita no DbSet, e NÃO só na coleção: <c>PurchaseInvoiceKey</c> é nulável, então
    /// a relação é opcional e o EF trataria a saída da coleção como órfã (FK nula) em vez de
    /// exclusão. A linha sumiria do Include e continuaria na tabela.
    ///
    /// A descrição do produto é RE-RESOLVIDA, e não copiada, pelo mesmo motivo do nome do emitente
    /// acima: o value help a grava com group ID null e o PATCH chega com a descrição do produto
    /// ANTERIOR (ou em branco, numa linha nova). Ver <c>PurchaseInvoiceLineGuard</c>.
    /// </summary>
    private async Task SyncItemsAsync(PurchaseInvoice existing, PurchaseInvoice entity)
    {
        var incoming = entity.Items.ToList();

        var removed = existing.Items
            .Where(current => incoming.All(i => i.Key != current.Key))
            .ToList();

        foreach (var line in removed)
        {
            existing.Items.Remove(line);
            db.Context.PurchaseInvoicesItems.Remove(line);
        }

        foreach (var line in incoming)
        {
            var current = line.Key is null
                ? null
                : existing.Items.FirstOrDefault(x => x.Key == line.Key);

            if (current is null)
            {
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

                // Add no DbSet, e NÃO em existing.Items: Key é ValueGeneratedOnAdd, e uma entidade
                // com chave já preenchida entrando pela NAVEGAÇÃO é lida pelo EF como registro
                // existente sendo reanexado — vira UPDATE de linha inexistente. O Add explícito
                // força o estado Added; a coleção do pai é preenchida pelo fixup.
                db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
                {
                    Key = line.Key ?? Guid.NewGuid(),
                    PurchaseInvoiceKey = existing.Key,
                    ItemCode = line.ItemCode,
                    ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
                        itemService, line.ItemCode, line.ItemName),
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    UnitOfMeasureCode = line.UnitOfMeasureCode,
                    SalesInvoiceItemKey = line.SalesInvoiceItemKey,
                    PurchaseInvoiceItemOriginKey = line.PurchaseInvoiceItemOriginKey,
                    PurchaseContractKey = line.PurchaseContractKey,
                });

                continue;
            }

            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

            // Capturado ANTES da atribuição: depois dela a comparação é sempre falsa.
            var itemCodeChanged = current.ItemCode != line.ItemCode;

            current.ItemCode = line.ItemCode;
            current.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
                itemService, line.ItemCode, line.ItemName, itemCodeChanged);
            current.Quantity = line.Quantity;
            current.UnitPrice = line.UnitPrice;
            current.UnitOfMeasureCode = line.UnitOfMeasureCode;
            current.SalesInvoiceItemKey = line.SalesInvoiceItemKey;
            current.PurchaseInvoiceItemOriginKey = line.PurchaseInvoiceItemOriginKey;
            current.PurchaseContractKey = line.PurchaseContractKey;
        }
    }
}
