using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.StorageCustody;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Confirma o documento de entrada: fecha para edição e o torna definitivo para a conciliação.
///
/// Nesta fase a confirmação SÓ transiciona o status — o gate de edição já é o valor: a devolução
/// antiga não tinha nenhum, e qualquer documento era alterável para sempre.
///
/// A Fase 3 pendura aqui o efeito da natureza de operação sobre o contrato de compra, sem mexer
/// nesta máquina de estados.
///
/// Documento eletrônico de terceiro: conferência da chave e, em Produção, consulta à SEFAZ (spec terceiro-chave §8).
/// </summary>
public class PurchaseInvoicesConfirmService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    IBusinessPartnerService businessPartnerService,
    SupplierNfeAuthorizationService supplierNfe,
    FinancialDocumentsFirmSyncService firmSync,
    FinancialDocumentsOtherPurchaseService otherPurchaseTitles,
    PurchaseInvoicesTaxApplyService taxApply,
    FiscalStockPeriodGuard? periodGuard = null)
{
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);

    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Somente documento pendente pode ser confirmado.");

        // A entrada cai no estoque fiscal na data de entrada (PostingDate ?? IssueDate): mês fechado recusa.
        await _periodGuard.EnsureOpenAsync(invoice, FiscalStockPeriodGuard.StockDate(invoice));

        // Emitir é o que confirma (spec D3): na filial com a regra ativa, o documento de emissão própria só
        // confirma com a NF-e autorizada — é o que a emissão chama. O de terceiro tem a conferência própria logo abaixo.
        if (invoice.IssuerType == DocumentIssuerType.Own && invoice.NfeStatus != NfeStatus.Authorized &&
            await gate.IsActiveAsync(invoice.BranchCode))
            throw new DefaultException("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.");

        // Documento eletrônico de terceiro (spec terceiro-chave §7/§8): a chave é conferida de novo e, com a filial em
        // Produção, a NF-e do fornecedor precisa estar autorizada na SEFAZ. A recusa deixa o documento Pendente.
        // O CT-e é conferido em qualquer filial (a chave é obrigatória); a NF-e só com a regra ativa.
        var gateActive = await gate.IsActiveAsync(invoice.BranchCode);
        if (SupplierNfeKeyGuard.AppliesTo(invoice) && (invoice.TaxDocumentKind == TaxDocumentKind.Cte || gateActive))
        {
            SupplierNfeKeyGuard.Ensure(invoice, (await businessPartnerService.GetByIdAsync(invoice.CardCode))?.TaxId);
            // O CT-e não é consultado na SEFAZ (spec 2026-10-17): só a conferência local da chave.
            if (invoice.TaxDocumentKind == TaxDocumentKind.Nfe && gateActive)
                await supplierNfe.EnsureAuthorizedAsync(invoice);
        }

        // A escrituração/entrada de custódia precisa continuar batendo com o romaneio (spec 2026-10-12 §4.4).
        var lines = await db.Context.PurchaseInvoicesItems
            .Where(l => l.PurchaseInvoiceKey == invoice.Key).ToListAsync();

        // CT-e (spec 2026-10-17): as regras do documento valem de novo na confirmação.
        if (PurchaseInvoiceCteRules.IsCte(invoice))
            await PurchaseInvoiceCteRules.EnsureAsync(db.Context, invoice, lines);
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, lines);

        // Spec 2026-10-17 §2: DIFAL sem a alíquota interna da UF não confirma; senão regrava o DIFAL (a UF pode ter sido cadastrada depois).
        await taxApply.RefreshDifalForConfirmAsync(invoice, lines);

        // Spec 2026-10-17: a regra do complemento de ICMS vale de novo na confirmação (a natureza ou a nota original podem
        // ter mudado desde a gravação). Não cria título (D5). O de valor revalida dentro do EnqueueForValueComplementAsync.
        if (invoice.InvoiceType == PurchaseInvoiceType.IcmsComplement)
            await taxApply.EnsureComplementAsync(invoice, lines);

        // Spec 2026-10-13 D11: o complemento de valor cria os títulos firmes aqui, no MESMO SaveChanges da confirmação.
        await firmSync.EnqueueForValueComplementAsync(invoice, lines, userName);

        // Spec 2026-10-17 §3: a "outra compra" cria as parcelas a pagar e os títulos de retenção, também no MESMO SaveChanges.
        // Qualquer recusa (retenção que consome a nota, credor ausente) sai antes de gravar: o documento segue Pendente.
        await otherPurchaseTitles.EnqueueAsync(invoice, lines, userName);

        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.ApprovedAt = DateTime.Now;
        invoice.ApprovedBy = userName;

        await db.SaveChangesAsync();
    }
}
