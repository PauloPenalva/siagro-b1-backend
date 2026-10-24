namespace SiagroB1.Fiscal.Reinf;

/// <summary>
/// Gera o Id do evento: "ID" + tpInsc(1) + CNPJ (raiz + 000000, 14) + AAAAMMDDHHMMSS + sequencial(5).
/// O sequencial reinicia a cada segundo. Thread-safe.
/// </summary>
public class ReinfEventIdGenerator
{
    private readonly object _lock = new();
    private string _lastStamp = string.Empty;
    private int _sequence;

    /// <param name="brasiliaNow">Horário de Brasília (a conversão é do chamador).</param>
    public string Next(string taxIdRoot, DateTime brasiliaNow)
    {
        var stamp = brasiliaNow.ToString("yyyyMMddHHmmss");
        int seq;
        lock (_lock)
        {
            if (stamp != _lastStamp) { _lastStamp = stamp; _sequence = 0; }
            seq = ++_sequence;
        }
        return $"ID1{ReinfFormat.Root(taxIdRoot).PadRight(8, '0')}000000{stamp}{seq:00000}";
    }
}
