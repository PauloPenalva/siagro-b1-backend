using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Vincula um adiantamento a OUTRO contrato. Sempre o adiantamento inteiro — parcial exigiria
/// partir o título em dois e encosta na amortização da Fase 2.
///
/// Depois da troca o contrato antigo fica sem adiantamento e o cancelamento passa; o contrato
/// novo herda o adiantamento e passa a ser protegido pelo mesmo guard.
///
/// O índice único filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin cobre apenas
/// Nature = Provisional, então adiantamento migrando de contrato nunca colide com ele.
/// </summary>
public class FinancialAdvancesRelinkContractService(
    IUnitOfWork db,
    FinancialDocumentChangeLogService changeLog)
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
}
