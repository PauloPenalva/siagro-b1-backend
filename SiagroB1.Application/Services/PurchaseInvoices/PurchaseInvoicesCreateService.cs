using SiagroB1.Application.Services.DocNumbers;
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
    IItemService itemService,
    PurchaseInvoicesTaxApplyService taxApply,
    DocNumberSequenceService numberSequenceService)
{
    public async Task ExecuteAsync(PurchaseInvoice invoice, string userName, bool nfeReturn = false)
    {
        if (invoice.Items.Count == 0)
            throw new DefaultException("Informe ao menos um item no documento de entrada.");

        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5): em toda filial.
        foreach (var item in invoice.Items)
            InvoiceLineChargeRules.Ensure(item);

        // O documento nunca nasce emitido, venha o que vier no corpo.
        PurchaseInvoiceNfeLock.ResetIssuanceFields(invoice);

        // A autorização da NF-e do fornecedor só o confirmar grava; emissão própria é sempre NF-e.
        invoice.SupplierNfeProtocol = null;
        invoice.SupplierNfeCheckedAt = null;
        if (invoice.IssuerType == DocumentIssuerType.Own)
            invoice.TaxDocumentKind = TaxDocumentKind.Nfe;

        // Só o "Devolver" (PurchaseInvoicesNfeReturnCreateService) marca a devolução de compra; o corpo da
        // API nunca — senão um POST tiraria a nota da confirmação pela emissão.
        invoice.IsNfeReturn = nfeReturn && invoice.InvoiceType == PurchaseInvoiceType.Return;

        if (invoice.IssuerType == DocumentIssuerType.Own && invoice.InvoiceType == PurchaseInvoiceType.Return &&
            !invoice.IsNfeReturn && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            throw new DefaultException(
                "Na filial que emite NF-e pelo Siagro, a devolução de compra é feita pelo botão Devolver, no detalhe do documento de entrada.");

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
        //
        // O parceiro é carregado UMA vez: serve ao nome e à condição de pagamento padrão.
        var partner = await businessPartnerService.GetByIdAsync(invoice.CardCode);

        // Documento eletrônico de terceiro: chave coerente com o fornecedor, número e série (spec terceiro-chave §7).
        // Antes da unicidade: o guard normaliza a chave colada com espaços.
        if (SupplierNfeKeyGuard.AppliesTo(invoice) && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            SupplierNfeKeyGuard.Ensure(invoice, partner?.TaxId);

        await PurchaseInvoiceChaveNFe.EnsureFreeAsync(db, invoice.ChaveNFe, invoice.Key);

        if (string.IsNullOrWhiteSpace(invoice.CardName))
            invoice.CardName = partner?.CardName;

        // Condição padrão do fornecedor (NF-e STANDALONE), como no documento de saída. A devolução de
        // compra não tem pagamento (tPag 90).
        if (!invoice.IsNfeReturn)
            invoice.PaymentConditionCode ??= partner?.PaymentConditionCode;

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

        // Documento de terceiro com o XML: todo nItem informado precisa existir na nota do fornecedor (é o que a
        // devolução referencia). Só na filial que emite NF-e pelo Siagro; fora dela o documento grava como veio.
        if (await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        // Tributos pela natureza, ANTES de gravar: as guardas recusam com mensagem de negócio.
        await taxApply.ApplyAsync(invoice, invoice.Items);

        // Valor declarado do terceiro Normal: a soma das linhas, como na emissão própria (o da tela não vale).
        if (PurchaseInvoiceDeclaredTotal.AppliesTo(invoice) && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            PurchaseInvoiceDeclaredTotal.Apply(invoice, invoice.Items);

        // Número interno sequencial (DE000001) para toda entrada, de qualquer tipo. Nasce aqui — por último, depois de todas as recusas, para não abrir buraco na sequência — e nunca muda;
        // o que vier no corpo é descartado (o update também não o copia).
        invoice.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.PurchaseInvoice);
        invoice.InvoiceNumber = await numberSequenceService.GetDocNumber((Guid) invoice.DocNumberKey);

        await db.Context.PurchaseInvoices.AddAsync(invoice);
        await db.SaveChangesAsync();
    }
}
