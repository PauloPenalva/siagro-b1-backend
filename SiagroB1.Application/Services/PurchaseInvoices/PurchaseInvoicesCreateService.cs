using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Registra o documento de entrada.
///
/// Nesta fase é documento de CONTROLE: não grava linha de ledger, não recalcula contrato e não toca
/// em romaneio. O efeito de negócio chega na Fase 3, pelo <c>UsageEffect</c> da natureza de
/// operação de cada linha.
/// </summary>
public class PurchaseInvoicesCreateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService)
{
    public async Task ExecuteAsync(PurchaseInvoice invoice, string userName)
    {
        if (invoice.Items.Count == 0)
            throw new DefaultException("Informe ao menos um item no documento de entrada.");

        await EnsureChaveNFeIsFreeAsync(invoice.ChaveNFe, invoice.Key);

        invoice.CreatedAt = DateTime.Now;
        invoice.CreatedBy = userName;
        invoice.InvoiceStatus = InvoiceStatus.Pending;

        // Só resolve pelo cadastro o que não veio: o nome lido do XML é o que consta NA NOTA, e
        // vale mais que o do cadastro para um documento fiscal de terceiro.
        //
        // VAZIO conta como "não veio", e não dá para trocar por `??=`: a tela manda "" e não null.
        // O value help copia a descrição com group ID null de propósito — o campo desnormalizado é
        // do servidor — e o create() do UI5 é obrigado a declarar a propriedade para a primeira
        // digitação não abrir "Must not change a property before it has been read". Com `??=` o
        // nome do emitente ficava em branco na tela E no banco.
        if (string.IsNullOrWhiteSpace(invoice.CardName))
            invoice.CardName =
                (await businessPartnerService.GetByIdAsync(invoice.CardCode))?.CardName;

        // Mesma história do nome do emitente, uma linha por vez: a descrição escolhida no value
        // help não entra no deep-insert, e sem isto a linha gravava com o produto certo e a
        // descrição em branco. Descrição vinda do XML é preservada.
        foreach (var item in invoice.Items)
        {
            item.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
                itemService, item.ItemCode, item.ItemName);

            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, item.PurchaseContractKey, item.ItemCode, invoice.CardCode);
        }

        await db.Context.PurchaseInvoices.AddAsync(invoice);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Uma chave de NF-e, um documento registrado. O índice único no banco é a rede de segurança;
    /// esta checagem existe para a mensagem sair legível em pt-BR.
    ///
    /// Documento CANCELADO não segura a chave — relançar é caminho legítimo, e é por isso que o
    /// índice do banco também é filtrado por status.
    ///
    /// Chave vazia ou em branco é tratada como AUSENTE. Sem isso, dois documentos digitados à mão
    /// sem chave colidiriam entre si numa query de igualdade sobre string vazia.
    /// </summary>
    private async Task EnsureChaveNFeIsFreeAsync(string? chaveNFe, Guid key)
    {
        if (string.IsNullOrWhiteSpace(chaveNFe))
            return;

        var duplicated = await db.Context.PurchaseInvoices
            .AnyAsync(x => x.ChaveNFe == chaveNFe &&
                           x.InvoiceStatus != InvoiceStatus.Cancelled &&
                           x.Key != key);

        if (duplicated)
            throw new DefaultException(
                $"Já existe documento de entrada com a chave de NF-e {chaveNFe}.");
    }
}
