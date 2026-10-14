using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um título firme (ou de devolução) aglutinado no PROVISÓRIO de uma fixação (spec 2026-10-13, títulos firmes, regras
/// 9–13). <see cref="Amount"/> é ASSINADO: firme positivo, devolução negativa. A linha é REMOVIDA ao desfazer; o rastro
/// fica no ledger (abatimento + estorno) e no log dos dois títulos.
/// <para>As duas FKs para FINANCIAL_DOCUMENTS são declaradas à mão no AppDbContext, sem navegação e sem coleção
/// inversa no título (entraria no EDM).</para>
/// </summary>
[Table("FINANCIAL_DOCUMENT_COMPOSITIONS")]
public class FinancialDocumentComposition : BaseEntity
{
    public Guid ProvisionalDocumentKey { get; set; }

    public Guid ComposedDocumentKey { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal Amount { get; set; }
}
