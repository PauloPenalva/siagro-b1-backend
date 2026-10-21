using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FastReport;
using FastReport.Export;
using FastReport.Export.PdfSimple.PdfCore;
using FastReport.Export.PdfSimple.PdfObjects;
using FastReport.Table;
using FastReport.Utils;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Mesma saída do <c>PDFSimpleExport</c> do FastReport OpenSource 2026.1.3 (cada página vira uma imagem JPEG
/// de 300 dpi), mas reaproveitando o Bitmap da página.
///
/// Por quê: o PDFSimpleExport cria um Bitmap novo por página (A4 a 300 dpi ≈ 35 MB) e o libgdiplus do Linux
/// não devolve essa memória no Dispose — o processo crescia ~35 MB por página para sempre (provado em
/// 14/10/2026 com System.Drawing puro). Aqui existe um Bitmap por tamanho de página, mantido pelo processo e
/// apagado entre páginas; a memória fica constante.
///
/// Diferenças intencionais: o fundo é limpo em branco (o original começa transparente e grava máscara
/// SMask quando sobra pixel transparente — no papel o resultado é o mesmo) e não há deduplicação por hash
/// de páginas idênticas além da comparação direta dos bytes do JPEG.
/// </summary>
public sealed class ReusableBitmapPdfExport : ExportBase
{
    private const int ImageDpi = 300;
    private const long JpegQuality = 90;
    private const float PointsPerMm = 2.83464f;

    /// <summary>Bitmaps livres por tamanho; cada exportação pega um emprestado e devolve no fim.</summary>
    private static readonly Dictionary<(int W, int H), Stack<Bitmap>> Pool = new();
    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");

    private readonly List<((int W, int H) Size, Bitmap Bitmap)> borrowed = [];
    private readonly Dictionary<string, PdfIndirectObject> imagesByContent = new();

    private Bitmap? pageBitmap;
    private Graphics? pageGraphics;
    private PdfContents? pageContent;
    private PdfPage? pdfPage;
    private PdfPages? pdfPages;
    private PdfIndirectObject? pdfPagesLink;
    private PdfWriter? pdfWriter;
    private readonly float scaleFactor = ImageDpi / 96f;

    public string? Title { get; set; }

    public ReusableBitmapPdfExport()
    {
        ShowProgress = false;
    }

    protected override string GetFileFilter() => "PDF (*.pdf)|*.pdf";

    protected override void Start()
    {
        base.Start();
        pdfWriter = new PdfWriter(Stream);
        pdfWriter.Begin();
        var catalog = new PdfCatalog();
        pdfPages = new PdfPages();
        pdfPagesLink = pdfWriter.Prepare(pdfPages);
        catalog.Pages = pdfPagesLink;
        pdfWriter.Prepare(catalog);
        imagesByContent.Clear();
    }

    protected override void ExportPageBegin(ReportPage page)
    {
        base.ExportPageBegin(page);
        var widthMm = ExportUtils.GetPageWidth(page);
        var heightMm = ExportUtils.GetPageHeight(page);

        pdfPage = new PdfPage
        {
            Parent = pdfPagesLink,
            MediaBox = new RectangleF(0, 0, widthMm * PointsPerMm, heightMm * PointsPerMm)
        };

        var size = ((int)(widthMm * scaleFactor * Units.Millimeters), (int)(heightMm * scaleFactor * Units.Millimeters));
        pageBitmap = Rent(size);
        pageGraphics = Graphics.FromImage(pageBitmap);
        pageGraphics.Clear(Color.White);
        pageGraphics.TranslateTransform(scaleFactor * page.LeftMargin * Units.Millimeters,
            scaleFactor * page.TopMargin * Units.Millimeters, MatrixOrder.Append);
        pageContent = new PdfContents();

        using (var background = new TextObject())
        {
            background.Fill = page.Fill;
            background.Left = -page.LeftMargin * Units.Millimeters;
            background.Top = -page.TopMargin * Units.Millimeters;
            background.Width = widthMm * Units.Millimeters;
            background.Height = heightMm * Units.Millimeters;
            ExportObj(background);
        }

        if (page.Watermark.Enabled && !page.Watermark.ShowImageOnTop) AddImageWatermark(page);
        if (page.Watermark.Enabled && !page.Watermark.ShowTextOnTop) AddTextWatermark(page);

        if (page.Border.Lines != BorderLines.None)
        {
            using var border = new TextObject();
            border.Border = page.Border;
            border.Left = 0;
            border.Top = 0;
            border.Width = (widthMm - page.LeftMargin - page.RightMargin) * PointsPerMm / 0.75f;
            border.Height = (heightMm - page.TopMargin - page.BottomMargin) * PointsPerMm / 0.75f;
            ExportObj(border);
        }
    }

    protected override void ExportBand(BandBase band)
    {
        base.ExportBand(band);
        ExportObj(band);
        foreach (Base obj in band.ForEachAllConvectedObjects(this))
        {
            if (obj is not TableColumn and not TableCell and not TableRow)
                ExportObj(obj);
        }
    }

    protected override void ExportPageEnd(ReportPage page)
    {
        base.ExportPageEnd(page);
        if (page.Watermark.Enabled && page.Watermark.ShowImageOnTop) AddImageWatermark(page);
        if (page.Watermark.Enabled && page.Watermark.ShowTextOnTop) AddTextWatermark(page);

        pageGraphics!.Dispose();
        pageGraphics = null;

        // PdfPage.AddImage é interno; a página tem uma única imagem, registrada como /Im0.
        var resources = (PdfDictionary)pdfPage!["Resources"];
        ((PdfDictionary)resources["XObject"])["Im0"] = WritePageImage(pageBitmap!);
        const string name = "/Im0";
        var width = ExportUtils.GetPageWidth(page) * PointsPerMm;
        var height = ExportUtils.GetPageHeight(page) * PointsPerMm;
        pageContent!.Append("q").AppendLine();
        pageContent.Append(width).Append(" 0 0 ").Append(height).Append(" 0 0 cm").AppendLine();
        pageContent.Append(name).Append(" Do").AppendLine();
        pageContent.Append("Q").AppendLine();

        pdfPage["Contents"] = pdfWriter!.Write(pageContent);
        pdfPages!.Kids.Add(pdfWriter.Write(pdfPage));
        pageBitmap = null;
    }

    protected override void Finish()
    {
        base.Finish();
        pdfWriter!.Write(new PdfInfo { Title = Title });
        pdfWriter.Finish();
        Stream.Flush();
        ReturnBorrowed();
    }

    protected override void Dispose(bool disposing)
    {
        pageGraphics?.Dispose();
        pageGraphics = null;
        ReturnBorrowed();
        base.Dispose(disposing);
    }

    private PdfIndirectObject WritePageImage(Bitmap bitmap)
    {
        using var jpeg = new MemoryStream();
        using (var parameters = new EncoderParameters(1))
        {
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
            bitmap.Save(jpeg, JpegCodec, parameters);
        }

        var bytes = jpeg.ToArray();
        var key = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(bytes));
        if (imagesByContent.TryGetValue(key, out var existing))
            return existing;

        var image = pdfWriter!.Write(new PdfImage { Width = bitmap.Width, Height = bitmap.Height, Stream = bytes });
        imagesByContent[key] = image;
        return image;
    }

    private void ExportObj(Base obj)
    {
        if (pageGraphics != null && obj is ReportComponentBase { Exportable: true } component)
            component.Draw(new FRPaintEventArgs(pageGraphics, scaleFactor, scaleFactor, Report.GraphicCache));
    }

    private RectangleF WatermarkArea(ReportPage page) => new(
        -page.LeftMargin * Units.Millimeters, -page.TopMargin * Units.Millimeters,
        ExportUtils.GetPageWidth(page) * Units.Millimeters, ExportUtils.GetPageHeight(page) * Units.Millimeters);

    private void AddImageWatermark(ReportPage page) =>
        page.Watermark.DrawImage(new FRPaintEventArgs(pageGraphics!, scaleFactor, scaleFactor, Report.GraphicCache),
            WatermarkArea(page), page.Report, false);

    private void AddTextWatermark(ReportPage page)
    {
        if (!string.IsNullOrEmpty(page.Watermark.Text))
            page.Watermark.DrawText(new FRPaintEventArgs(pageGraphics!, scaleFactor, scaleFactor, Report.GraphicCache),
                WatermarkArea(page), page.Report, false);
    }

    /// <summary>Um bitmap por tamanho é reaproveitado pela exportação inteira; páginas seguintes o apagam.</summary>
    private Bitmap Rent((int W, int H) size)
    {
        foreach (var (s, b) in borrowed)
            if (s == size) return b;

        Bitmap? bitmap = null;
        lock (Pool)
        {
            if (Pool.TryGetValue(size, out var free) && free.Count > 0)
                bitmap = free.Pop();
        }

        bitmap ??= new Bitmap(size.W, size.H, PixelFormat.Format32bppArgb);
        borrowed.Add((size, bitmap));
        return bitmap;
    }

    private void ReturnBorrowed()
    {
        lock (Pool)
        {
            foreach (var (size, bitmap) in borrowed)
            {
                if (!Pool.TryGetValue(size, out var free))
                    Pool[size] = free = new Stack<Bitmap>();
                free.Push(bitmap);
            }
        }

        borrowed.Clear();
    }
}
