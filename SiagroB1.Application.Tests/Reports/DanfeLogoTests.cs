using System.Drawing;
using System.Drawing.Imaging;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// O NFeRetrato.frx da Zeus desenha o logo em StretchImage numa caixa retrato: sem o encaixe, um logo
/// largo ou quadrado sai esticado na vertical.
/// </summary>
public class DanfeLogoTests
{
    [Fact]
    public void Wide_logo_is_letterboxed_to_the_danfe_box_ratio()
    {
        var fitted = DanfeLogo.FitToBox(Png(800, 640, Color.Blue));

        using var image = Decode(fitted);
        Assert.Equal(DanfeLogo.BoxHeight / DanfeLogo.BoxWidth, (double)image.Height / image.Width, 2);
        Assert.Equal(800, image.Width);
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(400, 2).ToArgb());
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(400, image.Height - 3).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(400, image.Height / 2).ToArgb());
    }

    [Fact]
    public void Tall_logo_is_padded_on_the_sides()
    {
        var fitted = DanfeLogo.FitToBox(Png(200, 600, Color.Blue));

        using var image = Decode(fitted);
        Assert.Equal(DanfeLogo.BoxHeight / DanfeLogo.BoxWidth, (double)image.Height / image.Width, 2);
        Assert.Equal(600, image.Height);
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(2, 300).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(image.Width / 2, 300).ToArgb());
    }

    [Fact]
    public void Undecodable_logo_is_returned_unchanged()
    {
        var garbage = new byte[] { 1, 2, 3 };

        Assert.Same(garbage, DanfeLogo.FitToBox(garbage));
    }

    [Fact]
    public void Missing_logo_stays_missing()
    {
        Assert.Null(DanfeLogo.FitToBox(null));
    }

    private static byte[] Png(int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(color);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Bitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
