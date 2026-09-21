namespace SiagroB1.Domain.Enums;

/// <summary>
/// De onde o transbordo nasceu (GAC-1181). Não muda o que o transbordo FAZ — muda apenas de que
/// passo do fluxo ele veio, e é isso que a tela mostra ao operador.
/// </summary>
public enum TransshipmentOrigin
{
    /// <summary>Planejado: a carga já sai da origem sabendo que vai ser mexida no meio.</summary>
    Planned = 0,

    /// <summary>
    /// Nasceu de uma recusa com destino Transbordo: a carga já foi faturada e devolvida, e a
    /// mercadoria vai ser padronizada antes de seguir para o mesmo cliente ou outro.
    /// </summary>
    Refusal = 1,
}
