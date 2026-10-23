using System.Globalization;
using System.Text;

namespace SiagroB1.Fiscal.Sped;

/// <summary>Decimal do SPED com número de casas diferente do padrão (2).</summary>
public readonly record struct SpedDecimal(decimal Value, int Places);

/// <summary>
/// Escritor de registros do SPED (EFD): linhas "|REG|campo|...|" em ISO-8859-1 com CRLF.
/// Fecha o bloco 9 (9001, 9900, 9990, 9999) em <see cref="Finish"/>.
/// </summary>
public sealed class SpedRecordWriter
{
    private static readonly CultureInfo Pt = new("pt-BR");

    private readonly List<string> _lines = new();
    private readonly Dictionary<string, int> _counts = new();
    private readonly List<string> _order = new();

    public void Add(string register, params object?[] fields)
    {
        // O bloco 9 é gerado exclusivamente por Finish
        if (register.StartsWith('9'))
            throw new ArgumentException($"Registro {register} do bloco 9 é reservado ao Finish.", nameof(register));
        var sb = new StringBuilder("|").Append(register).Append('|');
        foreach (var f in fields)
            sb.Append(Format(f)).Append('|');
        _lines.Add(sb.ToString());
        if (!_counts.TryAdd(register, 1)) _counts[register]++;
        else _order.Add(register);
    }

    public int CountOf(string register) => _counts.GetValueOrDefault(register);

    public int CountOfBlock(char block) =>
        _counts.Where(kv => kv.Key[0] == block).Sum(kv => kv.Value);

    public byte[] Finish()
    {
        var all = new List<string>(_lines);
        var types = new List<string>(_order);
        foreach (var r in new[] { "9001", "9900", "9990", "9999" })
            if (!types.Contains(r)) types.Add(r);

        all.Add("|9001|0|");
        // 9900 uma por tipo de registro, inclusive os do bloco 9
        foreach (var t in types)
        {
            var count = t switch
            {
                "9001" or "9990" or "9999" => 1,
                "9900" => types.Count,
                _ => _counts[t],
            };
            all.Add($"|9900|{t}|{count}|");
        }
        var block9 = all.Count - _lines.Count + 2; // + 9990 e 9999
        all.Add($"|9990|{block9}|");
        all.Add($"|9999|{all.Count + 1}|");

        var text = string.Concat(all.Select(l => l + "\r\n"));
        return Encoding.Latin1.GetBytes(text);
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => Clean(s),
        decimal d => d.ToString("F2", Pt),
        SpedDecimal sd => sd.Value.ToString("F" + sd.Places, Pt),
        DateOnly d => d.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, Pt),
        _ => Clean(value.ToString()!),
    };

    // Remove o separador e quebras de linha, que corromperiam o arquivo
    private static string Clean(string s) =>
        s.Replace("|", "").Replace("\r", "").Replace("\n", "").Trim();
}
