using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Usuário do SAP Business One (tabela OUSR).
///
/// Somente leitura: em modo SAPB1 a manutenção do cadastro é feita no SAP e o SiagroB1 apenas
/// espelha <c>USER_CODE</c>, <c>U_NAME</c> e <c>E_Mail</c> na tabela USERS.
/// </summary>
[Table("OUSR")]
public class SapUser
{
    /// <summary>
    /// <c>short</c>, e não <c>int</c>: OUSR.USERID é <c>smallint</c> no SAP. Mapear como int faz a
    /// leitura estourar com "Unable to cast object of type 'System.Int16' to type 'System.Int32'"
    /// - erro que só aparece contra o banco real, nunca em teste com provider em memória.
    /// </summary>
    [Key]
    [Column("USERID")]
    public short Id { get; set; }

    /// <summary>Código do usuário no SAP - equivale a USERS.Username.</summary>
    [Column("USER_CODE", TypeName = "NVARCHAR(25)")]
    public required string UserCode { get; set; }

    /// <summary>Nome do usuário no SAP - equivale a USERS.FullName.</summary>
    [Column("U_NAME", TypeName = "NVARCHAR(155)")]
    public string? UserName { get; set; }

    [Column("E_Mail", TypeName = "NVARCHAR(100)")]
    public string? Email { get; set; }

    /// <summary>"Y" quando o usuário está travado no SAP.</summary>
    [Column("Locked", TypeName = "CHAR(1)")]
    public string? Locked { get; set; }

    [Column("SUPERUSER", TypeName = "CHAR(1)")]
    public string? SuperUser { get; set; }
}
