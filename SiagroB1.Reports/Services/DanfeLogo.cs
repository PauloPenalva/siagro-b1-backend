using System.Drawing;
using System.Drawing.Imaging;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Encaixa o logo na proporção da caixa do emitente do NFeRetrato.frx. O layout da Zeus (vendorizado
/// sem alteração) desenha o logo em <c>SizeMode="StretchImage"</c> numa caixa retrato de
/// <see cref="BoxWidth"/> x <see cref="BoxHeight"/>: um logo largo ou quadrado sairia esticado na
/// vertical. Aqui o logo é centralizado num fundo branco já com essa proporção, e o "esticar" do
/// layout passa a não deformar nada. Se o layout mudar de caixa, atualize as constantes.
/// </summary>
public static class DanfeLogo
{
    public const double BoxWidth = 91.25;
    public const double BoxHeight = 116.18;

    /// <summary>
    /// PNG com o logo centralizado na proporção da caixa; o próprio logo quando ele não decodifica
    /// (um logo ruim custa o logo, nunca o DANFE — a Zeus é quem decide o que fazer com ele).
    /// </summary>
    public static byte[]? FitToBox(byte[]? logo)
    {
        if (logo is null)
            return null;

        try
        {
            using var input = new MemoryStream(logo, writable: false);
            using var source = Image.FromStream(input);

            var ratio = BoxHeight / BoxWidth;
            var width = source.Width;
            var height = (int)Math.Round(width * ratio);
            if (height < source.Height)
            {
                height = source.Height;
                width = (int)Math.Round(height / ratio);
            }

            using var canvas = new Bitmap(width, height);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
                g.DrawImage(source, (width - source.Width) / 2, (height - source.Height) / 2, source.Width, source.Height);
            }

            using var output = new MemoryStream();
            canvas.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception)
        {
            return logo;
        }
    }
}
