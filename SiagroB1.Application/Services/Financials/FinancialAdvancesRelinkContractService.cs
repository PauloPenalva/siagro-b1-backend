using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Vincula um adiantamento a OUTRO contrato.
///
/// Sem nada aplicado, move o adiantamento INTEIRO (comportamento original): o contrato antigo fica
/// sem adiantamento e o cancelamento passa; o contrato novo herda o adiantamento e passa a ser
/// protegido pelo mesmo guard.
///
/// Spec 2026-10-14 (regra 8, D3): com algo aplicado, DESMEMBRA — o original fica no contrato com o aplicado, e só o
/// crédito disponível vai para um adiantamento NOVO no destino (OriginType = FinancialDocument, OriginKey = original),
/// pago pela transferência em par (AdvanceTransfer de saída no original, de entrada no novo). É o único lugar onde
/// AdvanceTransfer nasce (regra 12). Desfaz-se pelo "Estornar baixa" da transferência (regra 9).
///
/// O índice único filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin cobre apenas
/// Nature = Provisional, então adiantamento migrando ou nascendo noutro contrato nunca colide com ele.
/// </summary>
public class FinancialAdvancesRelinkContractService(
    IUnitOfWork db,
    FinancialDocumentChangeLogService changeLog,
    DocNumberSequenceService docNumberSequence,
    SiagroB1.Application.Services.Accounting.Postings.IAccountingPostingService? accounting = null)
{
    public async Task ExecuteAsync(
        Guid documentKey, string targetContractType, Guid targetContractKey, string userName)
    {
        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException(
                $"O documento {document.Code} não é um adiantamento. Só adiantamento troca de contrato.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        var isPurchase = string.Equals(targetContractType, "Purchase", StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(targetContractType, "Sales", StringComparison.OrdinalIgnoreCase);

        if (!isPurchase && !isSales)
            throw new ApplicationException("Tipo de contrato inválido. Informe Purchase ou Sales.");

        // Direção cruzada é erro de NEGÓCIO: título a pagar é do produtor, a receber é do cliente.
        if (isPurchase && document.Direction != FinancialDirection.Payable)
            throw new ApplicationException(
                "Um título a receber só pode ser vinculado a um contrato de venda.");

        if (isSales && document.Direction != FinancialDirection.Receivable)
            throw new ApplicationException(
                "Um título a pagar só pode ser vinculado a um contrato de compra.");

        string targetCode, targetCardCode, targetBranchCode;
        CurrencyType targetCurrency;
        ContractStatus? targetStatus; // Status é nullable no modelo (PurchaseContract/SalesContract).

        if (isPurchase)
        {
            var contract = await db.Context.PurchaseContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == targetContractKey)
                           ?? throw new NotFoundException("Contrato de compra não encontrado.");

            targetCode = contract.Code ?? string.Empty;
            targetCardCode = contract.CardCode;
            targetBranchCode = contract.BranchCode ?? string.Empty;
            targetCurrency = contract.StandardCurrency ?? CurrencyType.Brl;
            targetStatus = contract.Status;
        }
        else
        {
            var contract = await db.Context.SalesContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == targetContractKey)
                           ?? throw new NotFoundException("Contrato de venda não encontrado.");

            targetCode = contract.Code ?? string.Empty;
            targetCardCode = contract.CardCode;
            targetBranchCode = contract.BranchCode ?? string.Empty;
            targetCurrency = contract.StandardCurrency ?? CurrencyType.Brl;
            targetStatus = contract.Status;
        }

        var currentKey = isPurchase ? document.PurchaseContractKey : document.SalesContractKey;

        if (currentKey == targetContractKey)
            throw new ApplicationException(
                $"O adiantamento {document.Code} já está vinculado ao contrato {targetCode}.");

        if (targetStatus != ContractStatus.Approved)
            throw new ApplicationException(
                $"O contrato {targetCode} precisa estar aprovado para receber o adiantamento.");

        // O dinheiro é DAQUELE parceiro: é este guard que impede mover crédito de um produtor
        // para o contrato de outro.
        if (!string.Equals(targetCardCode, document.CardCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O contrato {targetCode} é de outro parceiro ({targetCardCode}). O adiantamento " +
                $"é de {document.CardCode} e só pode migrar entre contratos do mesmo parceiro.");

        if (!string.Equals(targetBranchCode, document.BranchCode ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O contrato {targetCode} é da filial {targetBranchCode} e o adiantamento da " +
                $"filial {document.BranchCode}: a filial precisa ser a mesma.");

        if (targetCurrency != document.Currency)
            throw new ApplicationException(
                $"O contrato {targetCode} é em {targetCurrency} e o adiantamento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        if (document.AppliedAmount > 0m)
        {
            await SplitAsync(document, isPurchase, targetContractKey, targetCode, userName);
            return;
        }

        var oldCode = document.OriginDocNumber;

        document.PurchaseContractKey = isPurchase ? targetContractKey : null;
        document.SalesContractKey = isSales ? targetContractKey : null;
        document.OriginDocNumber = targetCode;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        // PaymentTermsText NÃO muda: é a cópia de "onde pagar" do contrato de origem, e para um
        // adiantamento já pago foi por ali que o dinheiro saiu.

        changeLog.Register(documentKey, FinancialDocumentChangeLogFields.Contract,
            oldCode, targetCode, userName);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Regra 8: adiantamento NOVO no destino com o crédito disponível, pago pela transferência em par; o original fica
    /// com crédito 0. Tudo num SaveChanges (o recálculo enxerga o título e as linhas ainda pendentes).
    /// </summary>
    private async Task SplitAsync(
        FinancialDocument original, bool isPurchase, Guid targetContractKey, string targetCode, string userName)
    {
        var credit = original.AvailableAdvanceAmount;
        if (credit <= 0m)
            throw new ApplicationException(
                $"O adiantamento {original.Code} não tem crédito disponível para vincular a outro contrato.");

        // Número buscado ANTES do SaveChanges e só depois de todas as validações: Dapper em conexão própria com UPDLOCK.
        var assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
            TransactionCode.FinancialDocument, null, null, 50,
            c => db.Context.FinancialDocuments.AnyAsync(x => x.Code == c)));
        var docNumberKey = assignment.DocNumberKey;
        var code = assignment.Code;
        var now = DateTime.Now;

        var split = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = code,
            DocNumberKey = docNumberKey,
            BranchCode = original.BranchCode,
            Direction = original.Direction,
            Nature = FinancialDocumentNature.Advance,
            Status = FinancialDocumentStatus.Open,
            CardCode = original.CardCode,
            CardName = original.CardName,
            DocumentDate = now,
            DueDate = original.DueDate,
            Currency = original.Currency,
            NetAmount = credit,
            OriginType = FinancialDocumentOrigin.FinancialDocument,
            OriginKey = original.Key,
            OriginDocNumber = targetCode,
            PurchaseContractKey = isPurchase ? targetContractKey : null,
            SalesContractKey = isPurchase ? null : targetContractKey,
            PaymentTermsText = original.PaymentTermsText,
            Comments = $"Crédito não aplicado do adiantamento {original.Code}.",
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        };
        db.Context.FinancialDocuments.Add(split);
        await SiagroB1.Application.Services.Accounting.Postings.FinancialDocumentPosting.CreateAsync(accounting, db.Context, split, userName);

        db.Context.FinancialSettlements.AddRange(
            Transfer(original, split, credit, now, userName),
            Transfer(split, original, credit, now, userName));
        await SiagroB1.Application.Services.Accounting.Postings.SettlementPosting.EnqueuePendingAsync(accounting, db.Context, userName);

        changeLog.Register(original.Key, FinancialDocumentChangeLogFields.AdvanceTransfer, null,
            FinancialDocumentsComposeService.Describe(split.Code, credit), userName);
        changeLog.Register(split.Key, FinancialDocumentChangeLogFields.AdvanceTransfer, null,
            FinancialDocumentsComposeService.Describe(original.Code, credit), userName);

        original.UpdatedAt = now;
        original.UpdatedBy = userName;

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, original.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, split.Key);

        await db.SaveChangesAsync();
    }

    /// <summary>Uma ponta da transferência: Amount positivo, sem conta, RelatedDocumentKey = a outra ponta.</summary>
    private static FinancialSettlement Transfer(
        FinancialDocument document, FinancialDocument other, decimal amount, DateTime now, string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = document.Key,
        FinancialAccountCode = null,
        SettlementDate = now.Date,
        Amount = amount,
        Origin = FinancialSettlementOrigin.AdvanceTransfer,
        RelatedDocumentKey = other.Key,
        DocumentReference = other.Code,
        CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
    };
}
