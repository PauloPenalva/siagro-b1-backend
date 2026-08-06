using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Linha do documento de entrada.
///
/// <see cref="ItemCode"/> e <see cref="UnitOfMeasureCode"/> são NULÁVEIS aqui, ao contrário de
/// <see cref="SalesInvoiceItem"/>: o código vem do emitente e pode não existir no cadastro local —
/// quem vale para a conferência é a linha de origem.
///
/// Os campos fiscais (natureza de operação, CFOP, NCM, CST, impostos) chegam na Fase 2, e as
/// amarrações a contrato de compra e a romaneio na Fase 3, junto com o value help e a coluna de
/// divergência que as consomem.
/// </summary>
[Table("PURCHASE_INVOICES_ITEMS")]
public class PurchaseInvoiceItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseInvoiceKey { get; set; }
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    [Column(TypeName = "VARCHAR(30)")]
    public string? ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "VARCHAR(4)")]
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>
    /// A AMARRAÇÃO da devolução, feita à mão pelo operador: a linha do documento de SAÍDA que esta
    /// linha espelha.
    ///
    /// Nulável por dois motivos, ambos legítimos: a linha nasce da importação do XML sem origem
    /// definida — o layout da NF-e não carrega esse vínculo — e a entrada NORMAL não tem origem
    /// alguma.
    /// </summary>
    public Guid? SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    /// <summary>Linha de remessa apontando a linha da NF de venda futura que a antecipou.</summary>
    public Guid? PurchaseInvoiceItemOriginKey { get; set; }
    public virtual PurchaseInvoiceItem? PurchaseInvoiceItemOrigin { get; set; }

    [NotMapped]
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);

    /// <summary>
    /// Quebra apurada da linha de ORIGEM — o número que o fiscal deveria espelhar.
    /// </summary>
    /// <remarks>
    /// Depende de <see cref="SalesInvoiceItem"/> CARREGADO. Sem o Include a navegação vem null e
    /// isto devolve 0 em silêncio, fazendo toda linha parecer divergente. Quem carrega é o
    /// <c>PurchaseInvoicesGetService</c>.
    /// </remarks>
    [NotMapped]
    public decimal AssessedShortage => SalesInvoiceItem?.AssessedShortage ?? 0m;

    /// <summary>
    /// Devolvido − quebra apurada. Zero é o caso em que fiscal e físico batem; diferente de zero a
    /// tela avisa, mas NÃO impede gravar — arredondamento e devolução parcial são legítimos, e quem
    /// decide é o usuário.
    /// </summary>
    [NotMapped]
    public decimal Difference =>
        decimal.Round(Quantity - AssessedShortage, 3, MidpointRounding.ToEven);
}
