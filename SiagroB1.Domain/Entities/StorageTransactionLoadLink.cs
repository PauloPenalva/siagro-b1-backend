using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// GAC-1216: a carga em que um romaneio viajou, lida da view <c>STORAGE_TRANSACTIONS_LOAD_LINKS</c>.
/// Só leitura — existe para as listas mostrarem, filtrarem e ordenarem pela carga no SQL.
/// </summary>
/// <remarks>
/// É uma view, e não uma coluna gravada no romaneio, porque a carga chega a ele por seis vínculos
/// diferentes (montagem, recusa, transbordo, perna de venda da Expedição, troca de liberação, nota
/// conferida na descarga) que mudam a cada troca/cancelamento/estorno: uma cópia gravada teria de
/// ser mantida em todos esses pontos. A regra mora na migration que cria a view.
/// <para>Romaneio que não passou por carga não tem linha aqui.</para>
/// </remarks>
public class StorageTransactionLoadLink
{
    [Key]
    public Guid StorageTransactionKey { get; set; }

    public Guid ShipmentLoadKey { get; set; }

    public string? LoadCode { get; set; }
}
