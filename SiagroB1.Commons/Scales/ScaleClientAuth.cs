using System.Security.Cryptography;
using System.Text;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Autenticação do SiagroB1.Client no canal WebSocket da balança.
///
/// O canal nasceu sem autenticação porque vivia só na rede interna. Quando o Client roda no PC da
/// balança e alcança o servidor pelo Gateway, quem descobrir o código de uma balança recebe a
/// configuração do indicador e consegue injetar peso - e peso injetado vira romaneio. A chave é o
/// que fecha isso.
///
/// Chave não configurada libera a conexão, para não quebrar o desenvolvimento nem as instalações
/// que seguem em rede interna. É falha aberta de propósito, e o aviso no boot do SiagroB1.Web é o
/// que impede que passe despercebida.
/// </summary>
public static class ScaleClientAuth
{
    /// <summary>Header do handshake. O Gateway repassa headers de requisição por padrão.</summary>
    public const string HeaderName = "X-Scale-Client-Key";

    /// <summary>Chave de configuração, a mesma no SiagroB1.Web e no SiagroB1.Client.</summary>
    public const string ConfigurationKey = "TruckScale:ClientKey";

    public static bool IsAuthorized(string? configuredKey, string? presentedKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            return true;

        if (string.IsNullOrEmpty(presentedKey))
            return false;

        // Comparação de tempo fixo porque é um segredo: `==` retorna no primeiro byte diferente.
        // FixedTimeEquals exige mesmo tamanho, então o tamanho da chave segue observável - o
        // conteúdo, não.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configuredKey),
            Encoding.UTF8.GetBytes(presentedKey));
    }
}
