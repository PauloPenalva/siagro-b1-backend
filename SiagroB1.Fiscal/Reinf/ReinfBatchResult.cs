namespace SiagroB1.Fiscal.Reinf;

public sealed record ReinfEventResult(string EventId, bool Accepted, string? Receipt, IReadOnlyList<string> Occurrences, string RawXml);

public sealed record ReinfBatchResult(
    int ResponseCode,
    string? Description,
    string? Protocol,
    IReadOnlyList<string> Occurrences,
    IReadOnlyList<ReinfEventResult> Events,
    string RawXml);
