namespace SiagroB1.Domain.Interfaces;

/// <summary>HTML completo → bytes de PDF. A implementação real usa Chromium headless.</summary>
public interface IHtmlToPdfRenderer
{
    Task<byte[]> RenderAsync(string html, CancellationToken ct = default);
}
