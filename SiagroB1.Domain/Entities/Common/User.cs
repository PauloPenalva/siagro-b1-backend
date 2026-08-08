using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("USERS")]
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(Username), IsUnique = true)]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    
    [Column(TypeName = "VARCHAR(50)")]
    public required string Username { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(256)")]
    public string? PasswordHash { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(100)")]
    public string FullName { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(100)")]
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;
    
    public bool IsAdmin { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Tema escolhido pelo usuário (sap_fiori_3, sap_horizon_dark, ...).</summary>
    [Column(TypeName = "VARCHAR(30)")]
    public string? Theme { get; set; }

    /// <summary>Foto do avatar. Nula quando o usuário não subiu nenhuma - a UI mostra as iniciais.</summary>
    public byte[]? PhotoContent { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? PhotoContentType { get; set; }

    public virtual ICollection<UserProfile> Profiles { get; set; } = [];

    /// <summary>
    /// Senha em claro, apenas para transporte no POST /odata/Users - o serviço de criação a
    /// converte em <see cref="PasswordHash"/> e a descarta.
    ///
    /// <c>[NotMapped]</c> tira a propriedade do banco E do EDM do OData — o
    /// ODataConventionModelBuilder honra o atributo. Para a tela de cadastro conseguir enviar o
    /// campo, o EDM a readiciona à mão em <c>ODataConfigurations.ConfigureODataEntities</c>.
    /// </summary>
    [NotMapped]
    public string? Password { get; set; }
}