using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Minimal IWebHostEnvironment for services that only need ContentRootPath/WebRootPath.
/// </summary>
public class TestWebHostEnvironment : IWebHostEnvironment
{
    public TestWebHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = Path.Combine(contentRootPath, "wwwroot");
    }

    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "SiagroB1.Application.Tests";
    public string ContentRootPath { get; set; }
    public string WebRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
