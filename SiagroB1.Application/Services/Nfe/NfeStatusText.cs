namespace SiagroB1.Application.Services.Nfe;

/// <summary>Texto de situação/retorno da NF-e gravado no documento (colunas de 500).</summary>
public static class NfeStatusText
{
    public static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
