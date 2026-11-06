using Microsoft.Extensions.Configuration;

namespace SiagroB1.Infra;

/// <summary>
/// Liga/desliga o módulo Mercado (cotações) por implantação. Chave ausente vale desligado,
/// para não expor o módulo a clientes que não o contrataram.
/// </summary>
public static class MarketModule
{
    public const string ConfigKey = "Modules:Market:Enabled";

    public static bool IsEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration[ConfigKey]?.Trim(), out var enabled) && enabled;
}
