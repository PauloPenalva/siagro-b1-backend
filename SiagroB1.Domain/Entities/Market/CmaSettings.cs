using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>
/// Conexão com o feed de cotações da CMA. Linha única (<see cref="Key"/> = 1) por banco de empresa.
/// A senha fica cifrada e nunca sai pela API.
/// </summary>
[Table("CMA_SETTINGS")]
public class CmaSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Key { get; set; } = 1;

    public bool Enabled { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string Host { get; set; } = "https://strfeedrt02.cma.com.br:443";

    [Column(TypeName = "VARCHAR(200)")]
    public string? UserName { get; set; }

    [Column(TypeName = "VARBINARY(512)")]
    public byte[]? PasswordEncrypted { get; set; }
}
