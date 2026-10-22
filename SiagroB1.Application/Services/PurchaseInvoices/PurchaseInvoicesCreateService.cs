using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Services.StorageDeposits;
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
    DocNumberSequenceService numberSequenceService,
    AccountingAssignmentValidator? dimensionValidator = null,
    AccountingAssignmentDefaultsResolver? dimensionDefaults = null)
{
    public async Task ExecuteAsync(PurchaseInvoice invoice, string userName, bool nfeReturn = false, bool counterNote = false)
    {
        if (invoice.Items.Count == 0)
            throw new DefaultException("Informe ao menos um item no documento de entrada.");

        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5): em toda filial.
        foreach (var item in invoice.Items)
            InvoiceLineChargeRules.Ensure(item);

        // Dimensões gerenciais: cabeçalho pelo fornecedor; linhas pelo cabeçalho, fornecedor e natureza. Antes de numerar:
        // a recusa não pode consumir número. No-op fora do STANDALONE.
        await AccountingAssignmentWiring.ApplyAsync(dimensionDefaults, dimensionValidator, invoice, null,
            invoice.CardCode, previous: null, documentDate: invoice.PostingDate ?? invoice.IssueDate);
        foreach (var item in invoice.Items)
            await AccountingAssignmentWiring.ApplyAsync(dimensionDefaults, dimensionValidator, item, invoice,
                invoice.CardCode, previous: null, documentDate: invoice.PostingDate ?? invoice.IssueDate);

        // O documento nunca nasce emitido, venha o que vier no corpo.
        PurchaseInvoiceNfeLock.ResetIssuanceFields(invoice);

        // Os vínculos com as NF-e transportadas nascem só do XML (SeedFromXmlAsync); o que vier no corpo é descartado.
        invoice.TransportedDocuments = [];

        // O Own vira NF-e logo abaixo, e o CT-e sumiria em silêncio: recusa antes (spec 2026-10-17).
        if (invoice.IssuerType == DocumentIssuerType.Own && PurchaseInvoiceCteRules.IsCte(invoice))
            throw new DefaultException(PurchaseInvoiceCteRules.OwnIssuerMessage);

        // A autorização da NF-e do fornecedor só o confirmar grava; emissão própria é sempre NF-e.
        invoice.SupplierNfeProtocol = null;
        invoice.SupplierNfeCheckedAt = null;
        if (invoice.IssuerType == DocumentIssuerType.Own)
            invoice.TaxDocumentKind = TaxDocumentKind.Nfe;

        // Só o "Devolver" (PurchaseInvoicesNfeReturnCreateService) marca a devolução de compra; o corpo da
        // API nunca — senão um POST tiraria a nota da confirmação pela emissão.
        invoice.IsNfeReturn = nfeReturn && invoice.InvoiceType == PurchaseInvoiceType.Return;

        // Só o "Gerar contra-nota" (PurchaseInvoicesCounterNoteCreateService) liga a entrada à escrituração do produtor;
        // o corpo da API nunca — senão um POST tiraria a compra dos relatórios e da emissão normal (spec 2026-10-10 §8).
        if (!counterNote)
        {
            invoice.ProducerInvoiceKey = null;
            foreach (var item in invoice.Items)
                item.ProducerInvoiceItemKey = null;
        }

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

        // O CT-e vale pela chave em qualquer filial, com ou sem a regra fiscal ativa (a NF-e segue a regra da filial).
        // Documento eletrônico de terceiro: chave coerente com o fornecedor, número e série (spec terceiro-chave §7).
        // Antes da unicidade: o guard normaliza a chave colada com espaços.
        if (SupplierNfeKeyGuard.AppliesTo(invoice) &&
            (invoice.TaxDocumentKind == TaxDocumentKind.Cte || await taxApply.IsBranchActiveAsync(invoice.BranchCode)))
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
                itemService, item.ItemCode, item.ItemName,
                otherPurchase: await taxApply.IsOtherPurchaseLineAsync(invoice, item));
        }

        // A natureza decide se a linha exige ou recusa o contrato (spec 2026-10-17 §1): antes da amarração, depois do nome.
        await taxApply.EnsureUsageContractRuleAsync(invoice, invoice.Items.ToList());

        foreach (var item in invoice.Items)
        {
            // Complemento próprio (spec 2026-10-14, D5): a regra do complemento valida o contrato depois de herdar o produto.
            if (item.ComplementedItemKey is null)
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, item.PurchaseContractKey, item.ItemCode, invoice.CardCode);
        }

        PurchaseInvoiceLineGuard.EnsureUnitOfMeasure(invoice, invoice.Items.ToList());

        // Complementos de valor (spec 2026-10-13 D11) e de ICMS (spec 2026-10-17) — e valor de complemento recusado nos
        // demais tipos.
        await taxApply.EnsureComplementAsync(invoice, invoice.Items.ToList());

        // Retorno de depósito: a linha precisa apontar a remessa, do mesmo produto e armazém, dentro do saldo (spec 2026-10-10 §3.3).
        await StorageDepositReturnGuard.EnsureAsync(db.Context, invoice.CardCode, null, invoice.Items.ToList());
        // Nota de custódia em armazém próprio (spec 2026-10-12 §4): romaneio, depositante, produto, filial, ordem e
        // quantidade; grava o papel no cabeçalho.
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, invoice.Items.ToList());

        // Documento de terceiro com o XML: todo nItem informado precisa existir na nota do fornecedor (é o que a
        // devolução referencia). Só na filial que emite NF-e pelo Siagro; fora dela o documento grava como veio.
        if (await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        // CT-e (spec 2026-10-17): documento de terceiro Normal, uma linha, UFs e tomador coerentes com a filial.
        if (PurchaseInvoiceCteRules.IsCte(invoice))
            await PurchaseInvoiceCteRules.EnsureAsync(db.Context, invoice, invoice.Items.ToList());

        // Funrural é calculado, nunca digitado: o que vier no corpo sai, e o ApplyAsync grava o da natureza quando a regra
        // está ativa (fora dela, a linha fica sem Funrural).
        foreach (var item in invoice.Items)
            PurchaseInvoiceFunrural.Clear(item);

        // Tributos pela natureza, ANTES de gravar: as guardas recusam com mensagem de negócio.
        await taxApply.ApplyAsync(invoice, invoice.Items);

        // Retenções de serviço (spec 2026-10-17 §2): depois dos tributos, porque a base é o total geral das linhas.
        await taxApply.ApplyWithholdingsAsync(invoice);
        await taxApply.ApplyDifalAsync(invoice);

        // Valor declarado do terceiro Normal: a soma das linhas, como na emissão própria (o da tela não vale).
        if (PurchaseInvoiceDeclaredTotal.AppliesTo(invoice) && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            PurchaseInvoiceDeclaredTotal.Apply(invoice, invoice.Items);

        // Número interno (DE000001) para toda entrada, de qualquer tipo. Nasce aqui — por último, depois de todas as recusas, para não abrir buraco na sequência — e nunca muda
        // (o update não copia série nem número). A série vem do corpo (ou a padrão); em série manual vale o InvoiceNumber digitado, em automática ele é descartado.
        var assignment = await numberSequenceService.AssignAsync(new DocNumberRequest(
            TransactionCode.PurchaseInvoice, invoice.DocNumberKey, invoice.InvoiceNumber, 9,
            c => db.Context.PurchaseInvoices.AnyAsync(x => x.InvoiceNumber == c)));
        invoice.DocNumberKey = assignment.DocNumberKey;
        invoice.InvoiceNumber = assignment.Code;

        // CT-e importado: uma chave de NF-e transportada por vínculo, já resolvida contra as saídas e entradas do sistema.
        if (PurchaseInvoiceCteRules.IsCte(invoice))
            await PurchaseInvoiceTransportedDocuments.SeedFromXmlAsync(db.Context, invoice);

        await db.Context.PurchaseInvoices.AddAsync(invoice);
        await db.SaveChangesAsync();

        // Entrada vinda da manifestação do destinatário: o DF-e da mesma filial e chave passa a apontar para o documento.
        // Depois do primeiro save porque o número (RowId) nasce no banco. A chave duplicada já foi recusada acima.
        await PurchaseInvoiceReceivedDfeLink.LinkAsync(db.Context, invoice);
        await db.SaveChangesAsync();
    }
}
