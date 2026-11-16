using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Configuração da integração com o SAP Business One (só SAPB1). Linha única (<see cref="Key"/> = 1) por
/// banco de empresa; sem linha valem os padrões abaixo.
/// </summary>
[Table("SAP_INTEGRATION_SETTINGS")]
public class SapIntegrationSettings
{
    public const int DefaultIntervalMinutes = 15;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 1440;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Key { get; set; } = 1;

    /// <summary>Liga a sincronização dos documentos de saída vinculados a invoices do SAP (GAC-1226).</summary>
    public bool InvoiceSyncEnabled { get; set; } = true;

    /// <summary>Intervalo mínimo entre duas sincronizações, de 1 a 1440 minutos.</summary>
    public int InvoiceSyncIntervalMinutes { get; set; } = DefaultIntervalMinutes;

    /// <summary>Última sincronização concluída (horário do servidor). Só o serviço grava.</summary>
    public DateTime? InvoiceSyncLastRunAt { get; set; }
}
