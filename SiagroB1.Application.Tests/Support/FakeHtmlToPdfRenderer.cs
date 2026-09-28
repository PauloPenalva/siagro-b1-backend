using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Support;

public sealed class FakeHtmlToPdfRenderer : IHtmlToPdfRenderer
{
    public string? LastHtml { get; private set; }
    public int Calls { get; private set; }

    public Task<byte[]> RenderAsync(string html, CancellationToken ct = default)
    {
        LastHtml = html;
        Calls++;
        return Task.FromResult(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }
}
