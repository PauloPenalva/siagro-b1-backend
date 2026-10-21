using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.Dimensions;
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
    FinancialDocumentsSettleService settleService,
    DimensionCodeValidator? dimensionValidator = null,
    DimensionDefaultsResolver? dimensionResolver = null,
    DimensionRequirementGuard? dimensionGuard = null)
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
        CommitMode commitMode = CommitMode.Auto,
        IDimensionFields? dimensions = null)
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

        var (account, accountCode) = await ValidateAccountAsync(financialAccountCode, paymentDate);

        if (account.Currency != currency)
            throw new ApplicationException(
                $"A conta {account.Code} é em {account.Currency} e o contrato em {currency}: a moeda precisa ser a mesma.");

        var partner = await businessPartnerService.GetByIdAsync(cardCode);

        return await CreatePaidAsync(
            isPurchase ? FinancialDirection.Payable : FinancialDirection.Receivable, cardCode, partner?.CardName,
            branchCode, currency, amount, dueDate, contractCode, isPurchase ? contractKey : null,
            isSales ? contractKey : null, paymentTerms, comments, accountCode, paymentDate, userName, commitMode, dimensions);
    }

    /// <summary>
    /// Adiantamento SEM contrato (spec 2026-10-19, parte 3): direção, parceiro e filial informados pelo usuário; moeda =
    /// a da conta financeira. Também nasce pago. Abate qualquer título não cancelado do mesmo parceiro, direção e moeda
    /// (ver <see cref="FinancialDocumentsApplyAdvanceService"/>) e pode ser vinculado a contrato depois.
    /// </summary>
    public async Task<FinancialDocument> ExecuteWithoutContractAsync(
        FinancialDirection direction,
        string cardCode,
        string branchCode,
        decimal amount,
        DateTime dueDate,
        string financialAccountCode,
        DateTime paymentDate,
        string? comments,
        string userName,
        CommitMode commitMode = CommitMode.Auto,
        IDimensionFields? dimensions = null)
    {
        if (amount <= 0m)
            throw new ApplicationException("O valor do adiantamento deve ser maior que zero.");

        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ApplicationException("Informe o parceiro do adiantamento.");

        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ApplicationException("Informe a filial do adiantamento.");

        var (account, accountCode) = await ValidateAccountAsync(financialAccountCode, paymentDate);

        var partnerCode = cardCode.Trim();
        var partner = await businessPartnerService.GetByIdAsync(partnerCode)
                      ?? throw new NotFoundException($"Parceiro {partnerCode} não encontrado.");

        return await CreatePaidAsync(
            direction, partnerCode, partner.CardName, branchCode.Trim(), account.Currency, amount,
            dueDate, null, null, null, string.Empty, comments, accountCode, paymentDate, userName, commitMode, dimensions);
    }

    // O adiantamento nasce PAGO: sem a baixa ele não tem crédito para compensar. A conta é validada antes de consumir
    // número e de abrir a transação.
    private async Task<(FinancialAccount Account, string Code)> ValidateAccountAsync(
        string financialAccountCode, DateTime paymentDate)
    {
        if (string.IsNullOrWhiteSpace(financialAccountCode))
            throw new ApplicationException("Informe a conta financeira do pagamento do adiantamento.");

        var accountCode = financialAccountCode.Trim();
        var account = await db.Context.FinancialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == accountCode)
                      ?? throw new ApplicationException($"Conta financeira {accountCode} não encontrada.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        if (paymentDate == default)
            throw new ApplicationException("Informe a data do pagamento do adiantamento.");

        return (account, accountCode);
    }

    private async Task<FinancialDocument> CreatePaidAsync(
        FinancialDirection direction, string cardCode, string? cardName, string branchCode, CurrencyType currency,
        decimal amount, DateTime dueDate, string? contractCode, Guid? purchaseContractKey, Guid? salesContractKey,
        string paymentTerms, string? comments, string accountCode, DateTime paymentDate, string userName,
        CommitMode commitMode, IDimensionFields? input)
    {
        // Padrão do parceiro + validação antes de consumir número e de abrir a transação.
        var dimensions = input is null ? new DimensionSnapshot() : DimensionSnapshot.From(input);
        await DimensionWiring.ApplyAsync(dimensionResolver, dimensionValidator, dimensions, null, cardCode, null, null);
        if (dimensionGuard is not null)
            await dimensionGuard.EnsureAsync([dimensions]);

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
            Direction = direction,
            Nature = FinancialDocumentNature.Advance,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = cardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = currency,
            NetAmount = decimal.Round(amount, 2, MidpointRounding.ToEven),
            OriginType = FinancialDocumentOrigin.Manual,
            OriginDocNumber = contractCode,
            PurchaseContractKey = purchaseContractKey,
            SalesContractKey = salesContractKey,
            PaymentTermsText = paymentTerms,
            Comments = comments,
            Dim1Code = dimensions.Dim1Code,
            Dim2Code = dimensions.Dim2Code,
            Dim3Code = dimensions.Dim3Code,
            Dim4Code = dimensions.Dim4Code,
            Dim5Code = dimensions.Dim5Code,
            LedgerAccountCode = dimensions.LedgerAccountCode,
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
