using System.Text;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>
/// Recorta o canal persistente da CMA em objetos JSON completos. O Java acumulava os chunks HTTP num
/// StringBuilder até o texto virar um JSON válido; aqui contamos chaves (fora de strings), o que também
/// separa dois objetos que cheguem no mesmo chunk e ignora quebras de linha entre eles.
/// </summary>
internal sealed class CmaChannelFramer
{
    private readonly StringBuilder _current = new();
    private int _depth;
    private bool _inString;
    private bool _escaped;

    public IReadOnlyList<string> Push(ReadOnlySpan<char> chunk)
    {
        var completed = new List<string>();
        foreach (var c in chunk)
        {
            if (_depth == 0)
            {
                if (c != '{')
                    continue; // espaço/quebra de linha entre mensagens
                _current.Clear();
            }

            _current.Append(c);

            if (_inString)
            {
                if (_escaped) _escaped = false;
                else if (c == '\\') _escaped = true;
                else if (c == '"') _inString = false;
                continue;
            }

            switch (c)
            {
                case '"': _inString = true; break;
                case '{': _depth++; break;
                case '}':
                    if (--_depth == 0)
                        completed.Add(_current.ToString());
                    break;
            }
        }

        return completed;
    }
}
