using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// O título FIRME acompanha o vínculo da Conciliação Fiscal de Compras (spec 2026-10-13, títulos firmes, regras 1–7):
/// nasce na gravação do vínculo (não na confirmação "Conciliado"), é recalculado se a quantidade mudar e é cancelado
/// se o vínculo sumir — composto, com dação ou com encontro de contas, recusa.
///
/// ENQUEUE-ONLY: o chamador (PurchaseFiscalReconciliationService) grava vínculo e título no MESMO SaveChanges.
/// ⚠️ Chame FORA de transação: DocNumberSequenceService roda por Dapper numa conexão própria, com UPDLOCK.
/// </summary>
public class FinancialDocumentsFirmSyncService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FinancialDocumentChangeLogService changeLog,
    IUsage usageService,
    IConfiguration configuration,
    FinancialDocumentsNettingService netting)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Regra 2 / D10: "gera financeiro" da NATUREZA da linha, lida agora — não a cópia gravada na linha, que só o
    /// cálculo de tributos da NF-e preenche. Em SAPB1 a natureza é o OUSG, sem a flag: conta como gera, decidido pelo
    /// MODO (o UsageModel do SAP traz false, não nulo). Linha sem natureza não gera.
    /// </summary>
    public async Task<bool> CreatesFinancialDocumentAsync(int? usageCode)
    {
        if (usageCode is not { } code) return false;
        if (!ErpMode.IsStandalone(configuration)) return true;
        return (await usageService.GetByIdAsync(code))?.CreatesFinancialDocument == true;
    }

    /// <summary>Cria (um por vínculo), recalcula ou cancela o firme do vínculo gravado/alterado.</summary>
    public async Task EnqueueSyncAsync(PurchaseFiscalReconciliation link, string userName)
    {
        var context = db.Context;

        var line = await context.PurchaseInvoicesItems.AsNoTracking()
                       .Include(l => l.PurchaseInvoice)
                       .FirstOrDefaultAsync(l => l.Key == link.PurchaseInvoiceItemKey)
                   ?? throw new NotFoundException("Linha de documento de entrada não encontrada.");
        var invoice = line.PurchaseInvoice
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        var firm = PurchaseFirmPayableRules.ActiveFirmsOf(context, [link.Key]).FirstOrDefault();

        // Regra 2 / D10: só com "gera financeiro" na natureza da linha e contrato na linha. Sem isso (ou NF cancelada),
        // um firme que exista de antes segue o vínculo desfeito.
        if (line.PurchaseContractKey is null || invoice.InvoiceStatus == InvoiceStatus.Cancelled ||
            !await CreatesFinancialDocumentAsync(line.UsageCode))
        {
            if (firm is not null)
                PurchaseFirmPayableRules.CancelLooseFirms(context, [link.Key], userName);
            return;
        }

        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);
        var complement = line.ItemCode is null
            ? null
            : await context.ItemComplements.AsNoTracking().FirstOrDefaultAsync(c => c.ItemCode == line.ItemCode);
        var factor = R.KgFactor(line.UnitOfMeasureCode, complement)
                     ?? throw new DefaultException(
                         $"Cadastre o fator da unidade {line.UnitOfMeasureCode} no Complemento do Produto {line.ItemCode} para vincular a NF {taxDocument}.");

        // Regra 4. Em SAPB1 o Funrural da linha é zero: o firme sai pelo bruto.
        var amount = PurchaseFirmPayableRules.Amount(link.Quantity, line.Quantity * factor, line.GrandTotal, line.FunruralValue);

        if (firm is not null)
        {
            // Regravar sem mudar o valor não toca no título — nem recusa o composto.
            if (firm.NetAmount == amount) return;

            // Regra 6: só o firme solto é recalculado.
            PurchaseFirmPayableRules.EnsureLoose(context, firm);

            changeLog.Register(firm.Key, FinancialDocumentChangeLogFields.NetAmount,
                firm.NetAmount.ToString("N2", PtBr), amount.ToString("N2", PtBr), userName);
            firm.NetAmount = amount;
            firm.UpdatedAt = DateTime.Now;
            firm.UpdatedBy = userName;
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, firm.Key);
            return;
        }

        var contract = await context.PurchaseContracts.AsNoTracking()
                           .Where(c => c.Key == line.PurchaseContractKey)
                           .Select(c => new { c.Code })
                           .FirstOrDefaultAsync()
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");
        var transactionCode = await context.StorageTransactions.AsNoTracking()
            .Where(t => t.Key == link.StorageTransactionKey)
            .Select(t => t.Code)
            .FirstOrDefaultAsync();

        // Regra 5: número ANTES do SaveChanges (conexão Dapper própria, fora de transação).
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
        var code = await docNumberSequence.GetDocNumber(docNumberKey);
        var issueDate = (invoice.IssueDate ?? DateTime.Today).Date;
        var now = DateTime.Now;

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = code,
            DocNumberKey = docNumberKey,
            BranchCode = invoice.BranchCode,
            Direction = FinancialDirection.Payable,
            // Regra 3: pelo sentido do documento — a conciliação já garantiu que casa com o romaneio (8/10/17 ou 9/16).
            Nature = invoice.InvoiceType == PurchaseInvoiceType.Return
                ? FinancialDocumentNature.PurchaseReturn
                : FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            CardCode = invoice.CardCode,
            CardName = invoice.CardName,
            // Firme não é pago direto: o vencimento que vale é o do provisório.
            DocumentDate = issueDate,
            DueDate = issueDate,
            // D12: o firme nasce na moeda do documento de entrada (BRL), não na do contrato.
            Currency = CurrencyType.Brl,
            NetAmount = amount,
            OriginType = FinancialDocumentOrigin.PurchaseFiscalReconciliation,
            OriginKey = link.Key,
            OriginDocNumber = contract.Code,
            PurchaseContractKey = line.PurchaseContractKey,
            PurchaseInvoiceKey = invoice.Key,
            StorageTransactionKey = link.StorageTransactionKey,
            StorageTransactionCode = transactionCode,
            TaxDocumentNumber = taxDocument,
            CreatedAt = now,
            CreatedBy = userName,
            UpdatedAt = now,
            UpdatedBy = userName,
        };

        context.FinancialDocuments.Add(document);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);

        // Regra 8: devolução nasce → compensação automática com o firme de origem solto (regras 18–19).
        if (document.Nature == FinancialDocumentNature.PurchaseReturn)
            await netting.EnqueueAutoAsync(document, line.PurchaseInvoiceItemOriginKey, userName);
    }

    /// <summary>Regra 7: vínculo removido → cancela o firme solto ou recusa (S1/P1).</summary>
    public void EnqueueRemoval(Guid linkKey, string userName) =>
        PurchaseFirmPayableRules.CancelLooseFirms(db.Context, [linkKey], userName);

    /// <summary>
    /// Spec 2026-10-13 D11: confirmar o documento "Complemento de valor" cria um firme por linha (origem PurchaseInvoice,
    /// OriginKey = linha), pelo valor da linha menos o Funrural, sem romaneio. Revalida as regras do documento (a
    /// natureza ou o contrato podem ter mudado desde a gravação). ENQUEUE-ONLY e fora de transação (numeração por Dapper).
    /// Idempotente por linha: o índice IX_FINANCIAL_DOCUMENTS_FirmOrigin (OriginType + OriginKey) é a segunda camada.
    /// </summary>
    public async Task EnqueueForValueComplementAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, string userName)
    {
        if (invoice.InvoiceType != PurchaseInvoiceType.ValueComplement)
            return;

        await PurchaseInvoiceComplement.EnsureAsync(
            invoice, lines, usageService, new TaxCalculationGate(db, configuration), db.Context);

        var context = db.Context;
        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);
        var issueDate = (invoice.IssueDate ?? DateTime.Today).Date;
        var contractKeys = lines.Select(l => l.PurchaseContractKey!.Value).Distinct().ToList();
        var contracts = await context.PurchaseContracts.AsNoTracking()
            .Where(c => contractKeys.Contains(c.Key))
            .Select(c => new { c.Key, c.Code })
            .ToDictionaryAsync(c => c.Key);

        foreach (var line in lines)
        {
            var lineKey = line.Key!.Value;
            if (PurchaseFirmPayableRules.ActiveValueComplementFirmsOf(context, [lineKey]).Count > 0)
                continue;

            var contract = contracts[line.PurchaseContractKey!.Value];
            var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
            var code = await docNumberSequence.GetDocNumber(docNumberKey);
            var now = DateTime.Now;

            var document = new FinancialDocument
            {
                Key = Guid.NewGuid(),
                Code = code,
                DocNumberKey = docNumberKey,
                BranchCode = invoice.BranchCode,
                Direction = FinancialDirection.Payable,
                Nature = FinancialDocumentNature.Firm,
                Status = FinancialDocumentStatus.Open,
                CardCode = invoice.CardCode,
                CardName = invoice.CardName,
                DocumentDate = issueDate,
                DueDate = issueDate,
                Currency = CurrencyType.Brl, // D12: moeda do documento de entrada
                NetAmount = decimal.Round(line.GrandTotal - line.FunruralValue, 2, MidpointRounding.ToEven),
                OriginType = FinancialDocumentOrigin.PurchaseInvoice,
                OriginKey = lineKey,
                OriginDocNumber = contract.Code,
                PurchaseContractKey = line.PurchaseContractKey,
                PurchaseInvoiceKey = invoice.Key,
                TaxDocumentNumber = taxDocument,
                CreatedAt = now,
                CreatedBy = userName,
                UpdatedAt = now,
                UpdatedBy = userName,
            };

            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
        }
    }
}
