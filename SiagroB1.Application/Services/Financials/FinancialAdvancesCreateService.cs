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
/// Nasce PAGO (spec 2026-10-19): a inclusão grava junto a baixa na conta financeira escolhida, na mesma transação. A
/// compensação no provisório só usa crédito pago (AvailableAdvanceAmount = baixado − aplicado), e a baixa é o que leva o
/// dinheiro ao extrato e à reconciliação da conta — sem lançamento manual à parte, que contaria o pagamento duas vezes.
///
/// FASE 2: a amortização contra o documento firme entra como linha de ledger com origem
/// AdvanceApplication, e AvailableAdvanceAmount passa a descontar o que já foi aplicado.
/// </summary>
public class FinancialAdvancesCreateService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService,
    FinancialDocumentsSettleService settleService)
{
    public async Task<FinancialDocument> ExecuteAsync(
        string contractType,
        Guid contractKey,
        decimal amount,
        DateTime dueDate,
        string financialAccountCode,
        DateTime paymentDate,
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

        // O adiantamento nasce PAGO: sem a baixa ele não tem crédito para compensar o provisório. A conta é validada
        // aqui, antes de consumir número e de abrir a transação; a moeda tem de ser a do contrato.
        if (string.IsNullOrWhiteSpace(financialAccountCode))
            throw new ApplicationException("Informe a conta financeira do pagamento do adiantamento.");

        var accountCode = financialAccountCode.Trim();
        var account = await db.Context.FinancialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == accountCode)
                      ?? throw new ApplicationException($"Conta financeira {accountCode} não encontrada.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        if (account.Currency != currency)
            throw new ApplicationException(
                $"A conta {account.Code} é em {account.Currency} e o contrato em {currency}: a moeda precisa ser a mesma.");

        if (paymentDate == default)
            throw new ApplicationException("Informe a data do pagamento do adiantamento.");

        var partner = await businessPartnerService.GetByIdAsync(cardCode);

        // Número buscado ANTES da transação: Dapper em conexão separada, segurando UPDLOCK.
        var assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
            TransactionCode.FinancialDocument, null, null, 50,
            c => db.Context.FinancialDocuments.AnyAsync(x => x.Code == c)));
        var docNumberKey = assignment.DocNumberKey;
        var code = assignment.Code;

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

            // Baixa na mesma transação: é ela que aparece na conta (extrato, reconciliação) e libera o crédito.
            await settleService.ExecuteAsync(
                advance.Key, accountCode, advance.NetAmount, paymentDate, 0m, 0m, 0m,
                documentReference: null, notes: "Pagamento do adiantamento na inclusão", userName,
                CommitMode.Deferred);

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
