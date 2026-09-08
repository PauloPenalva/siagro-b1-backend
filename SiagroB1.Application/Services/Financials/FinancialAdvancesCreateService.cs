using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Adiantamento a contrato: o único documento LIQUIDÁVEL da Fase 1.
///
/// Direção segue o lado do contrato — adiantamos ao produtor (compra → a pagar) ou recebemos do
/// cliente (venda → a receber). Valor e vencimento são informados pelo usuário; parceiro, moeda
/// e filial vêm do contrato.
///
/// Dois adiantamentos no mesmo contrato são legítimos, e o índice único de idempotência não os
/// barra porque filtra por Nature = Provisional.
///
/// FASE 2: a amortização contra o documento firme entra como linha de ledger com origem
/// AdvanceApplication, e AvailableAdvanceAmount passa a descontar o que já foi aplicado.
/// </summary>
public class FinancialAdvancesCreateService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<FinancialDocument> ExecuteAsync(
        string contractType,
        Guid contractKey,
        decimal amount,
        DateTime dueDate,
        string? comments,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (amount <= 0m)
            throw new ApplicationException("O valor do adiantamento deve ser maior que zero.");

        var isPurchase = string.Equals(contractType, "Purchase", StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(contractType, "Sales", StringComparison.OrdinalIgnoreCase);

        if (!isPurchase && !isSales)
            throw new ApplicationException("Tipo de contrato inválido. Informe Purchase ou Sales.");

        string cardCode, contractCode, branchCode, paymentTerms;
        CurrencyType currency;

        if (isPurchase)
        {
            var contract = await db.Context.PurchaseContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == contractKey)
                           ?? throw new NotFoundException("Contrato de compra não encontrado.");

            if (contract.Status != ContractStatus.Approved)
                throw new ApplicationException(
                    "O contrato precisa estar aprovado para receber adiantamento.");

            cardCode = contract.CardCode;
            contractCode = contract.Code ?? string.Empty;
            branchCode = contract.BranchCode ?? string.Empty;
            currency = contract.StandardCurrency ?? CurrencyType.Brl;
            paymentTerms = contract.PaymentTerms ?? string.Empty;
        }
        else
        {
            var contract = await db.Context.SalesContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == contractKey)
                           ?? throw new NotFoundException("Contrato de venda não encontrado.");

            if (contract.Status != ContractStatus.Approved)
                throw new ApplicationException(
                    "O contrato precisa estar aprovado para receber adiantamento.");

            cardCode = contract.CardCode;
            contractCode = contract.Code ?? string.Empty;
            branchCode = contract.BranchCode ?? string.Empty;
            currency = contract.StandardCurrency ?? CurrencyType.Brl;
            paymentTerms = contract.PaymentTerms ?? string.Empty;
        }

        var partner = await businessPartnerService.GetByIdAsync(cardCode);

        // Número buscado ANTES da transação: Dapper em conexão separada, segurando UPDLOCK.
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
        var code = await docNumberSequence.GetDocNumber(docNumberKey);

        var advance = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = code,
            DocNumberKey = docNumberKey,
            BranchCode = branchCode,
            Direction = isPurchase ? FinancialDirection.Payable : FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Advance,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = partner?.CardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = currency,
            NetAmount = decimal.Round(amount, 2, MidpointRounding.ToEven),
            OriginType = FinancialDocumentOrigin.Manual,
            OriginDocNumber = contractCode,
            PurchaseContractKey = isPurchase ? contractKey : null,
            SalesContractKey = isSales ? contractKey : null,
            PaymentTermsText = paymentTerms,
            Comments = comments,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialDocuments.Add(advance);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }

        return advance;
    }
}
