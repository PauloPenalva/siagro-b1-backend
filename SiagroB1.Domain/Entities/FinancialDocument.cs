using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento financeiro: uma obrigação a pagar ou a receber.
///
/// Na Fase 1 nasce PROVISÓRIO, a partir de cada fixação de preço confirmada, e é bloqueado
/// para baixa — representa o compromisso do contrato, não uma dívida exigível. A Fase 2 o
/// converte em FIRME pela confirmação do documento fiscal, e a partir daí OpenAmount de um
/// provisório significa "saldo a faturar".
///
/// A natureza ADVANCE (adiantamento) é a exceção da Fase 1: nasce desbloqueada e é liquidada
/// normalmente.
/// </summary>
[Table("FINANCIAL_DOCUMENTS")]
[Index(nameof(Code), IsUnique = true)]
[Index(nameof(Direction), nameof(Status), nameof(DueDate))]
[Index(nameof(CardCode))]
[Index(nameof(PurchaseContractKey))]
[Index(nameof(SalesContractKey))]
public class FinancialDocument : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }

    public FinancialDirection Direction { get; set; }

    public FinancialDocumentNature Nature { get; set; }

    /// <summary>
    /// Persistido-derivado de <see cref="SettledAmount"/>. Escritor ÚNICO:
    /// FinancialDocumentsRecalculateBalanceService. A exceção é <see cref="FinancialDocumentStatus.Canceled"/>,
    /// que só o cancelamento grava. Persistido, e não [NotMapped], porque as telas filtram e
    /// ordenam por ele NO SERVIDOR.
    /// </summary>
    public FinancialDocumentStatus Status { get; set; } = FinancialDocumentStatus.Open;

    /// <summary>SAP ENTITY — sem FK: em modo SAPB1 BUSINESS_PARTNERS está vazia.</summary>
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Emissão: data da aprovação do contrato ou da confirmação da fixação.</summary>
    public DateTime DocumentDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Obrigatório. Resolvido por <c>fixation.FinancialDueDate ?? contract.StandardCashFlowDate</c>
    /// e validado no gerador ANTES de qualquer escrita — documento sem vencimento não entra em
    /// fluxo de caixa nenhum.
    /// </summary>
    public DateTime DueDate { get; set; }

    [Column(TypeName = "INT DEFAULT 1")]
    public CurrencyType Currency { get; set; } = CurrencyType.Brl;

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal NetAmount { get; set; }

    /// <summary>
    /// Persistido-derivado: soma ASSINADA de FINANCIAL_SETTLEMENTS.Amount. Recalculado sempre
    /// por soma do ledger, NUNCA incrementalmente, e protegido por <see cref="RowVersion"/>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal SettledAmount { get; set; }

    public FinancialDocumentOrigin OriginType { get; set; }

    /// <summary>
    /// Chave da linha geradora — a FIXAÇÃO, na Fase 1. SEM FK e SEM navegação: a linha precisa
    /// sobreviver ao registro que ela narra, como ShipmentLoadMovement.SalesInvoiceKey.
    /// Junto com <see cref="OriginType"/> forma a chave de idempotência do índice único filtrado.
    /// </summary>
    public Guid? OriginKey { get; set; }

    /// <summary>Código do contrato, desnormalizado — a lista mostra a origem sem join.</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? OriginDocNumber { get; set; }

    /// <summary>
    /// Documento de entrada do FIRME (spec 2026-10-13, títulos firmes). Sem FK, como <see cref="OriginKey"/>: o
    /// título sobrevive ao registro que narra.
    /// </summary>
    public Guid? PurchaseInvoiceKey { get; set; }

    /// <summary>Romaneio do vínculo que gerou o firme. Sem FK.</summary>
    public Guid? StorageTransactionKey { get; set; }

    /// <summary>Código do romaneio, desnormalizado para a lista.</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? StorageTransactionCode { get; set; }

    /// <summary>"número-série" da NF, ou o número interno do documento sem NF (PurchaseFiscalReconciliationRules.TaxDocument).</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? TaxDocumentNumber { get; set; }

    /// <summary>
    /// Só no PROVISÓRIO: persistido-derivado, Σ FINANCIAL_DOCUMENT_COMPOSITIONS.Amount (assinado: firme +, devolução −).
    /// Escritor ÚNICO: FinancialDocumentsRecalculateBalanceService.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal ComposedAmount { get; set; }

    /// <summary>
    /// Só no ADIANTAMENTO (spec 2026-10-14): persistido-derivado, Σ das linhas AdvanceApplication e AdvanceTransfer de
    /// SAÍDA (e dos estornos delas). Escritor ÚNICO: FinancialDocumentsRecalculateBalanceService. Nos demais títulos fica 0.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal AppliedAmount { get; set; }

    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>
    /// Cópia de <c>fixation.PaymentDetails ?? contract.PaymentTerms</c>. É o texto que o
    /// financeiro lê para saber onde pagar. O cadastro estruturado de condição de pagamento é
    /// Fase 2, e vai entrar AO LADO deste campo, não no lugar dele.
    /// </summary>
    [Column(TypeName = "VARCHAR(1000)")]
    public string? PaymentTermsText { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Concorrência real: duas baixas simultâneas do mesmo documento passam pelos dois guards
    /// e só aqui a segunda falha.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<FinancialSettlement> Settlements { get; set; } = [];

    public ICollection<FinancialDocumentChangeLog> ChangeLogs { get; set; } = [];

    [NotMapped]
    public decimal OpenAmount =>
        decimal.Round(NetAmount - SettledAmount, 2, MidpointRounding.ToEven);

    /// <summary>
    /// Derivado, SEM coluna — uma coluna criaria uma segunda fonte de verdade. Provisório: bloqueado enquanto não houver
    /// composto a pagar (paga-se só o que está coberto por firme). Firme e devolução da conciliação: sempre — saem só
    /// por composição, dação ou encontro de contas. Demais (adiantamento, título a receber do washout): livres.
    /// </summary>
    [NotMapped]
    public bool IsBlockedForSettlement => Nature switch
    {
        FinancialDocumentNature.Provisional => AvailableToSettleAmount <= 0m,
        _ => IsPurchaseFirmPayable,
    };

    /// <summary>Provisório: composto − baixado (mínimo 0). Demais naturezas: o saldo em aberto.</summary>
    [NotMapped]
    public decimal AvailableToSettleAmount => Nature == FinancialDocumentNature.Provisional
        ? Math.Max(0m, decimal.Round(ComposedAmount - SettledAmount, 2, MidpointRounding.ToEven))
        : OpenAmount;

    /// <summary>
    /// Firme ou devolução nascidos da Conciliação Fiscal de Compras, ou firme nascido da confirmação do documento de
    /// entrada "Complemento de valor" (spec D11, origem PurchaseInvoice). O título a receber do washout também é Firm,
    /// mas de outra origem, e continua baixável em dinheiro. Só em memória (o EF não traduz) e fora do EDM.
    /// </summary>
    [NotMapped]
    public bool IsPurchaseFirmPayable =>
        (OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
         Nature is FinancialDocumentNature.Firm or FinancialDocumentNature.PurchaseReturn) ||
        (OriginType == FinancialDocumentOrigin.PurchaseInvoice && Nature == FinancialDocumentNature.Firm);

    [NotMapped]
    public bool IsOverdue => DueDate.Date < DateTime.Now.Date && OpenAmount > 0;

    /// <summary>
    /// Crédito de adiantamento disponível (spec 2026-10-14): o que foi pago menos o que já foi aplicado (compensado no
    /// provisório ou transferido no revínculo), nunca negativo. Nos demais títulos, 0.
    /// </summary>
    [NotMapped]
    public decimal AvailableAdvanceAmount => Nature == FinancialDocumentNature.Advance
        ? Math.Max(0m, decimal.Round(SettledAmount - AppliedAmount, 2, MidpointRounding.ToEven))
        : 0m;
}
