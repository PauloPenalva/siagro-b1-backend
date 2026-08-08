using System.Text;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Junta os pedaços que chegam do socket e devolve frames completos. O limite de tamanho protege
/// contra um terminador configurado errado, que faria o buffer crescer para sempre.
/// </summary>
public sealed class ScaleFrameBuffer(string terminator, int maxLength = 4096)
{
    private readonly StringBuilder _buffer = new();
    private readonly string _terminator = string.IsNullOrEmpty(terminator) ? "\n" : terminator;

    public IEnumerable<string> Append(string chunk)
    {
        _buffer.Append(chunk);

        var frames = new List<string>();
        var text = _buffer.ToString();

        int index;
        var consumed = 0;

        while ((index = text.IndexOf(_terminator, consumed, StringComparison.Ordinal)) >= 0)
        {
            frames.Add(text[consumed..index]);
            consumed = index + _terminator.Length;
        }

        _buffer.Clear();

        var tail = text[consumed..];

        if (tail.Length <= maxLength)
            _buffer.Append(tail);

        return frames;
    }
}
