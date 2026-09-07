using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

/// <summary>
/// Largura e ordem das colunas que um usuário ajustou numa tabela da UI (GAC-1163).
///
/// Uma linha por (usuário, tabela), e não um documento único no usuário: as abas do sistema ficam
/// abertas por horas, e com um documento só a segunda aba a gravar sobrescreveria o ajuste feito na
/// primeira. Assim o pior caso é "última escrita vence" DAQUELA tabela.
///
/// Sem FK para USERS de propósito, no mesmo padrão de USER_TRUCK_SCALES: a API só tem o username em
/// mãos (User.Identity.Name) e não existe ICurrentUser no projeto. A claim carrega sempre o
/// Username lido do banco, nunca o que o usuário digitou, então não há risco de duas linhas por
/// diferença de caixa.
/// </summary>
[Table("USER_TABLE_LAYOUTS")]
[Index(nameof(Username), nameof(TableKey), IsUnique = true)]
public class UserTableLayout
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(50)")]
    [MaxLength(50)]
    public required string Username { get; set; }

    /// <summary>
    /// Identidade da tabela na UI, no formato <c>&lt;viewName&gt;::[&lt;escopo&gt;--]&lt;localId&gt;</c> —
    /// ex. <c>siagrob1.view.purchaseContracts.Main::tablePurchaseContracts</c>.
    ///
    /// 200 e não 150: a maior chave medida no app tem ~120 caracteres (tabela dentro do diálogo de
    /// conciliação).
    /// </summary>
    [Column(TypeName = "VARCHAR(200)")]
    [MaxLength(200)]
    public required string TableKey { get; set; }

    /// <summary>
    /// Documento versionado: <c>{"Version":1,"Columns":[{"Key":"...","Width":"180px"}]}</c>.
    ///
    /// O array É a ordem — não há campo de posição, que só permitiria estados inconsistentes.
    /// <c>Width</c> ausente significa "largura padrão do XML", de modo que uma mudança futura no
    /// default da tela ainda chegue a quem já personalizou.
    /// </summary>
    public required string LayoutJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
