namespace SiagroB1.Domain.Enums;

/// <summary>
/// O que o operador está fazendo com a parada no armazém (GAC-1176). O PROCESSO é o mesmo do
/// transbordo (GAC-1181) — saída da carga, entrada pesada, quebra, crédito no armazém e liberação;
/// o tipo só dá o contexto certo na tela e decide como a parada termina.
/// </summary>
/// <remarks>
/// ⚠️ Persistido como <c>int</c>: as linhas anteriores ao GAC-1176 valem 0 (Transbordo).
/// </remarks>
public enum TransshipmentType
{
    /// <summary>Parada intermediária: a mercadoria é recarregada e segue para o cliente.</summary>
    Transshipment = 0,

    /// <summary>
    /// Transferência entre armazéns de terceiros: a mercadoria TERMINA a viagem no armazém e a
    /// carga é concluída sem faturamento. Não aceita Expedição de saída vinculada.
    /// </summary>
    Transfer = 1,
}
