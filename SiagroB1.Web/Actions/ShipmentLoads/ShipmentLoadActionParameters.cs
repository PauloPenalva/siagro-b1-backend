using System.Globalization;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Leitura dos parâmetros das actions de descarga e anexo da carga (GAC-1171).
/// </summary>
/// <remarks>
/// Existe porque os dois helpers daqui eram copy-paste entre os controllers, e copy-paste de
/// parser é onde a correção sai pela metade: o <c>DecodeFile</c> chegou a ser traduzido no
/// registro de descarga e esquecido no upload de anexo.
/// </remarks>
public static class ShipmentLoadActionParameters
{
    /// <summary>
    /// A tela manda <c>DischargeDate</c> como <c>yyyy-MM-dd</c> — é o que a Task 7 produz com
    /// <c>slice(0, 10)</c>. Nada além disso é aceito.
    /// </summary>
    private const string DateFormat = "yyyy-MM-dd";

    public const string InvalidDateMessage =
        "Data da descarga inválida. Informe a data no formato aaaa-mm-dd.";

    public const string MissingDateMessage = "Informe a data da descarga.";

    public const string UnreadableFileMessage =
        "Não foi possível ler o arquivo anexado. Envie o arquivo novamente.";

    /// <summary>
    /// Devolve <c>false</c> quando veio conteúdo que não é uma data <c>yyyy-MM-dd</c>, e
    /// <c>true</c> com <paramref name="date"/> nulo quando o parâmetro simplesmente não veio.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>TryParseExact</c>, nunca <c>TryParse</c>. Com <c>TryParse</c> + InvariantCulture a
    /// data brasileira não é recusada, é MAL INTERPRETADA: "05/09/2026" (5 de setembro) parseia
    /// como 9 de maio e grava silenciosamente a data errada. E o formato ISO com sufixo <c>Z</c>
    /// vira horário local (UTC-3) antes do <c>.Date</c>, voltando um dia.
    /// <para>
    /// Separar "não veio" de "veio ilegível" é o ponto: quem chama decide. No registro, a ausência
    /// cai no dia de hoje; na alteração ela é recusada, porque o serviço grava
    /// <c>DischargeDate</c> sem condição e carimbaria hoje por cima da data já registrada.
    /// </para>
    /// </remarks>
    public static bool TryParseDate(object? value, out DateTime? date)
    {
        date = null;

        // Parâmetro string do EDM é anulável: TryGetValue devolve true com valor nulo.
        if (value is null)
            return true;

        if (value is not string text)
            return false;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!DateTime.TryParseExact(
                text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        date = parsed.Date;

        return true;
    }

    /// <summary>
    /// O arquivo viaja em base64. Conteúdo corrompido é erro do payload, não falha do servidor — e
    /// a mensagem crua de <see cref="FormatException"/> chega ao usuário em inglês.
    /// </summary>
    public static bool TryDecodeFile(string base64, out byte[] file)
    {
        try
        {
            file = Convert.FromBase64String(base64);

            return true;
        }
        catch (FormatException)
        {
            file = [];

            return false;
        }
    }
}
