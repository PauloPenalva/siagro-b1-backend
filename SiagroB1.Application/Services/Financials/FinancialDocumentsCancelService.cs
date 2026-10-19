using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Cancela documento financeiro. CANCELA, NUNCA APAGA: apagar funcionaria e mataria o rastro —
/// é a tentação óbvia desta feature. Cancelar também é o que LIBERA a origem no índice único
/// filtrado, permitindo que a reabertura de contrato regenere o provisório.
/// </summary>
public class FinancialDocumentsCancelService(IUnitOfWork db)
{
    private AppDbContext Context => db.Context;

    /// <summary>Ponto de entrada da tela: cancela um documento e SALVA.</summary>
    public async Task ExecuteAsync(Guid key, string reason, string userName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do cancelamento.");

        var document = await Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} já está cancelado.");

        // Baixa (parcial ou integral) trava antes de qualquer outra regra: é a mensagem que o usuário precisa ver.
        if (document.SettledAmount != 0m)
            throw new ApplicationException(
                $"O título {document.Code} possui baixa financeira. Cancelamento não permitido.");

        // Spec 2026-10-15 (regra 4): o firme a receber nasce e morre com o documento de saída — cancelar à mão deixaria a
        // linha confirmada sem título, e a próxima sincronização criaria outro.
        if (document.IsSalesFirmReceivable)
            throw new ApplicationException(
                "Título firme do documento de saída nasce da confirmação: cancele ou estorne o documento de saída para cancelá-lo.");

        // Spec 2026-10-13 (títulos firmes): cancelar à mão deixaria o vínculo vivo sem título — e a próxima gravação
        // dele criaria outro. O caminho é desfazer o vínculo na Conciliação Fiscal de Compras.
        if (document.IsPurchaseFirmPayable && document.OriginType == FinancialDocumentOrigin.PurchaseInvoice)
            throw new ApplicationException(
                "Título firme do complemento de valor nasce da confirmação do documento de entrada: cancele ou estorne o documento para cancelá-lo.");

        if (document.IsPurchaseFirmPayable)
            throw new ApplicationException(
                "Título firme nasce e morre com o vínculo da Conciliação Fiscal de Compras: desfaça o vínculo para cancelá-lo.");

        // Spec 2026-10-17 §3: parcelas e retenções da outra compra nascem e morrem com o documento de entrada. O desfazer
        // (FinancialDocumentsOtherPurchaseService.EnqueueUndo) grava direto, sem passar por aqui.
        if (document.OriginType == FinancialDocumentOrigin.OtherPurchaseInvoice)
            throw new ApplicationException(
                "Título da outra compra nasce da confirmação do documento de entrada: estorne a confirmação ou cancele o documento para cancelá-lo.");

        // F4 (revisão final): cancelar o título de washout por aqui deixaria o washout Approved
        // reduzindo saldo e provisório sem título nenhum por trás — e um Estorno depois passaria
        // em silêncio, porque EnqueueCancelByKeyAsync trata documento já cancelado como no-op.
        if (document.OriginType == FinancialDocumentOrigin.PurchaseContractWashout)
            throw new ApplicationException(
                "Título gerado por washout não pode ser cancelado aqui. " +
                $"Estorne o washout no contrato de compra {document.OriginDocNumber}.");

        // Spec 2026-10-15 (washout de venda, §5.6): mesmo motivo do F4 — o washout aprovado ficaria reduzindo saldo e
        // provisório sem título nenhum por trás.
        if (document.OriginType == FinancialDocumentOrigin.SalesContractWashout)
            throw new ApplicationException(
                "Título gerado por washout não pode ser cancelado aqui. " +
                $"Estorne o washout no contrato de venda {document.OriginDocNumber}.");

        // Spec 2026-10-13 (títulos firmes): cancelar o provisório composto deixaria linhas de composição órfãs
        // abatendo os firmes.
        if (document.ComposedAmount != 0m)
            throw new ApplicationException(
                $"O documento {document.Code} tem composição. Desfaça a composição antes de cancelar.");

        // Título gerado pelo sistema nasce e morre com a sua origem (contrato, documento, desdobramento): pela tela só
        // sai o lançado à mão. As recusas acima ficam porque dizem ao usuário onde desfazer a origem.
        if (document.OriginType != FinancialDocumentOrigin.Manual)
            throw new ApplicationException(
                $"O título {document.Code} foi gerado automaticamente e não pode ser cancelado manualmente.");

        Cancel(document, reason, userName);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Enqueue-only, para os hooks de contrato: cancela o provisório de UMA origem e não salva.
    /// </summary>
    public async Task EnqueueCancelByOriginAsync(
        FinancialDocumentOrigin originType, Guid originKey, string reason, string userName)
    {
        var documents = await Context.FinancialDocuments
            .Where(x => x.OriginType == originType &&
                        x.OriginKey == originKey &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();

        foreach (var document in documents)
            Cancel(document, reason, userName);
    }

    /// <summary>
    /// Enqueue-only: cancela TODOS os provisórios abertos de um contrato, e — quando
    /// <paramref name="includeUnpaidAdvances"/> é true — também os adiantamentos SEM baixa.
    ///
    /// Adiantamento não pago é só uma promessa e morre junto com o contrato. Adiantamento PAGO
    /// nunca é alcançado aqui: ele é barrado antes, por
    /// <see cref="FinancialDocumentsContractCancellationGuardService"/>, que exige estorno,
    /// devolução ou revínculo.
    ///
    /// Só os dois serviços de CANCELAMENTO passam true. O ENCERRAMENTO de contrato de compra não chama mais este
    /// método (spec 2026-10-13, regra 28); o de venda continua chamando com o padrão false.
    /// </summary>
    public async Task EnqueueCancelByContractAsync(
        Guid? purchaseContractKey, Guid? salesContractKey, string reason, string userName,
        bool includeUnpaidAdvances = false)
    {
        var documents = await Context.FinancialDocuments
            .Where(x => (x.Nature == FinancialDocumentNature.Provisional ||
                         (includeUnpaidAdvances &&
                          x.Nature == FinancialDocumentNature.Advance &&
                          x.SettledAmount == 0m)) &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        ((purchaseContractKey != null && x.PurchaseContractKey == purchaseContractKey) ||
                         (salesContractKey != null && x.SalesContractKey == salesContractKey)))
            .ToListAsync();

        // Regra 27 (spec 2026-10-13, títulos firmes): cancelar contrato e retirar aprovação recusam provisório com
        // composição ou baixa. O encerramento não chega mais aqui (regra 28).
        var blocked = documents.FirstOrDefault(x =>
            x.Nature == FinancialDocumentNature.Provisional && (x.ComposedAmount != 0m || x.SettledAmount != 0m));
        if (blocked is not null)
            throw new ApplicationException(
                $"O título {blocked.Code} tem composição ou baixa: desfaça a composição e estorne as baixas antes de cancelar o contrato ou retirar a aprovação.");

        foreach (var document in documents)
            Cancel(document, reason, userName);
    }

    /// <summary>
    /// Enqueue-only, para a EXCLUSÃO de contrato em rascunho: cancela os provisórios ainda abertos
    /// e solta TODOS os documentos do contrato (chave do contrato = null). A FK para o contrato é
    /// NoAction e barrava a exclusão com erro 547, mesmo com o documento cancelado — e apagar o
    /// documento mataria o rastro. <c>OriginDocNumber</c> segue com o número do contrato excluído.
    ///
    /// Documento ativo que não seja provisório (adiantamento, por exemplo) é dinheiro de verdade
    /// prometido ao parceiro: barra a exclusão em vez de ficar sem contrato em silêncio.
    /// </summary>
    public async Task EnqueueReleaseContractForDeletionAsync(
        Guid? purchaseContractKey, Guid? salesContractKey, string userName)
    {
        if (purchaseContractKey is null && salesContractKey is null)
            throw new ArgumentException(
                "Informe a chave do contrato de compra ou de venda para liberar a exclusão.");

        var documents = await Context.FinancialDocuments
            .Where(x => (purchaseContractKey != null && x.PurchaseContractKey == purchaseContractKey) ||
                        (salesContractKey != null && x.SalesContractKey == salesContractKey))
            .ToListAsync();

        var blocking = documents
            .Where(x => x.Nature != FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled)
            .Select(x => x.Code)
            .ToList();

        if (blocking.Count > 0)
            throw new DefaultException(
                $"O contrato possui título financeiro ativo: {string.Join(", ", blocking)}. " +
                "Cancele o título ou vincule-o a outro contrato antes de excluir o contrato.");

        foreach (var document in documents)
        {
            if (document.Status != FinancialDocumentStatus.Canceled)
                Cancel(document, "Contrato excluído", userName);

            document.PurchaseContractKey = null;
            document.SalesContractKey = null;
        }
    }

    /// <summary>
    /// Enqueue-only: cancela UM documento pela chave — hoje, o título a receber do washout
    /// estornado. Recusa documento com baixa: o dinheiro já entrou, e cancelar o título apagaria
    /// o rastro dele. Documento já cancelado passa em silêncio (idempotente).
    /// </summary>
    public async Task EnqueueCancelByKeyAsync(Guid key, string reason, string userName)
    {
        var document = await Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Status == FinancialDocumentStatus.Canceled) return;

        if (document.SettledAmount != 0m)
            throw new ApplicationException(
                $"O documento {document.Code} possui baixas. Estorne a baixa antes.");

        Cancel(document, reason, userName);
    }

    internal static void Cancel(Domain.Entities.FinancialDocument document, string reason, string userName)
    {
        document.Status = FinancialDocumentStatus.Canceled;
        document.CancellationReason = reason;
        document.CanceledAt = DateTime.Now;
        document.CanceledBy = userName;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;
    }
}
