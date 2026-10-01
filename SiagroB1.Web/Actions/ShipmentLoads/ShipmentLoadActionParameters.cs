using System.Collections;
using System.Globalization;
using SiagroB1.Application.Services.ShipmentLoads;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Leitura dos parâmetros das actions de descarga e anexo da carga (GAC-1171), reaproveitado
/// pelo transbordo (GAC-1181) para o parsing de data — ver <see cref="TryParseDate"/>.
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

    /// <summary>
    /// GAC-1181: mensagens próprias do transbordo (<c>TransshipmentDate</c> do início,
    /// <c>EntryDate</c> do registro da entrada). Reaproveitar <see cref="InvalidDateMessage"/>/
    /// <see cref="MissingDateMessage"/> faria o operador ler "descarga" — outra tela, outro
    /// documento — ao errar a data de um transbordo.
    /// </summary>
    public const string TransshipmentInvalidDateMessage =
        "Data do transbordo inválida. Informe a data no formato aaaa-mm-dd.";

    public const string TransshipmentMissingDateMessage = "Informe a data do transbordo.";

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

    public const string MissingDistributionMessage =
        "Distribua o peso descarregado entre os documentos de saída.";

    /// <summary>
    /// Lê o rateio (GAC-1171, rateio): <c>SalesInvoiceItemKeys</c> (Collection(Edm.Guid)) e
    /// <c>Quantities</c> (Collection(Edm.Double)) como arrays PARALELOS, o precedente de
    /// ShipmentLoadsRefuse. Tamanhos diferentes são erro de montagem do payload, recusado aqui.
    /// </summary>
    /// <remarks>
    /// Um array de INTEIROS (<c>[20000, 15000]</c>) pode chegar como coleção de <c>int</c>/<c>long</c>.
    /// O cast direto para <c>double</c> devolveria lista vazia sem erro, e o ticket seria recusado por
    /// "rateio vazio" com o usuário vendo os números na tela.
    /// </remarks>
    public static bool TryReadDistribution(
        IDictionary<string, object> parameters,
        out List<ShipmentLoadDischargeLine> lines,
        out string? error)
    {
        lines = [];
        error = null;

        if (!parameters.TryGetValue("SalesInvoiceItemKeys", out var keysObj) || keysObj is not IEnumerable<Guid> keys ||
            !parameters.TryGetValue("Quantities", out var quantitiesObj) || quantitiesObj is not IEnumerable sequence)
        {
            error = MissingDistributionMessage;
            return false;
        }

        var quantities = new List<decimal>();

        foreach (var value in sequence)
        {
            if (!TryReadNumber(value, out var number))
            {
                error = $"Peso rateado inválido: {value}.";
                return false;
            }

            quantities.Add(number);
        }

        var keyList = keys.ToList();

        if (keyList.Count != quantities.Count)
        {
            error = "A lista de itens e a de pesos do rateio têm tamanhos diferentes.";
            return false;
        }

        lines = keyList
            .Select((key, index) => new ShipmentLoadDischargeLine(key, quantities[index]))
            .ToList();

        return true;
    }

    private static bool TryReadNumber(object? value, out decimal number)
    {
        switch (value)
        {
            case double d:
                number = (decimal)d;
                return true;
            case decimal m:
                number = m;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case float f:
                number = (decimal)f;
                return true;
            default:
                return decimal.TryParse(
                    value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
        }
    }
}
