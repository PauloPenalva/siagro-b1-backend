using Microsoft.Extensions.Localization;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Localizador de testes: devolve a própria chave como valor. Os testes que o usam
/// verificam qual exceção foi lançada, não o texto traduzido.
/// </summary>
public sealed class FakeStringLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(name, arguments), resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
