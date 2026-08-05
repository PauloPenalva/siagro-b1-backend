using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Linha da devolução do cliente, amarrada à linha do documento de saída que a originou.
///
/// A amarração é por LINHA porque os dois padrões de cliente cabem nela: quem emite uma nota
/// com várias linhas, cada uma referenciando uma NF de origem, e quem emite uma nota por NF de
/// origem — este vira o caso de uma linha só.
/// </summary>
[Table("CUSTOMER_RETURNS_ITEMS")]
public class CustomerReturnItem
{
    [Key]
    public Guid? Key { get; set; }

    public Guid? CustomerReturnKey { get; set; }
    public virtual CustomerReturn? CustomerReturn { get; set; }

    /// <summary>
    /// Produto como veio no XML do cliente. Informativo: o código do cliente pode não ser o
    /// mesmo do cadastro da Yokotobi, e quem vale para a conferência é a linha de origem.
    /// </summary>
    [Column(TypeName = "VARCHAR(30)")]
    public string? ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// A AMARRAÇÃO, feita à mão pelo operador. Nulável porque a linha nasce da importação do
    /// XML sem origem definida — o XML não carrega esse vínculo.
    /// </summary>
    public Guid? SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    [NotMapped]
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);

    /// <summary>
    /// Quebra apurada da linha de ORIGEM — o que o fiscal deveria espelhar.
    /// </summary>
    /// <remarks>
    /// Depende de <see cref="SalesInvoiceItem"/> CARREGADO. Sem o Include a navegação vem
    /// null e isto devolve 0 em silêncio, fazendo toda linha parecer divergente. Quem carrega
    /// é o <c>CustomerReturnsGetService</c>.
    /// </remarks>
    [NotMapped]
    public decimal AssessedShortage => SalesInvoiceItem?.AssessedShortage ?? 0m;

    /// <summary>
    /// Devolvido − quebra apurada. Zero é o caso em que fiscal e físico batem; diferente de
    /// zero a tela avisa, mas não impede gravar — arredondamento e devolução parcial são
    /// legítimos, e quem decide é o usuário.
    /// </summary>
    [NotMapped]
    public decimal Difference =>
        decimal.Round(Quantity - AssessedShortage, 3, MidpointRounding.ToEven);
}
