namespace SiagroB1.Commons.Scales;

/// <summary>
/// O caminho físico até o indicador: socket TCP ou porta serial. Só abre o fluxo de bytes; a
/// leitura dos quadros e a reconexão são iguais para os dois e ficam em
/// <see cref="ScaleIndicatorConnection"/>.
/// </summary>
public interface IScaleTransport
{
    /// <summary>Identificação para o log: "192.168.0.10:4001" ou "COM3 9600 8N1".</summary>
    string Description { get; }

    /// <summary>
    /// Tempo máximo sem peso válido antes de considerar o indicador fora do ar. Nulo desliga o
    /// prazo. Existe por causa da serial, que não avisa quando o cabo é desconectado.
    /// </summary>
    TimeSpan? SilenceTimeout { get; }

    /// <summary>Abre o fluxo. Descartar o fluxo devolvido fecha a conexão (ou a porta).</summary>
    Task<Stream> OpenAsync(CancellationToken ct);
}
