using System.Text;
using SiagroB1.Fiscal.Sped;
using Xunit;

namespace SiagroB1.Fiscal.Tests.Sped;

public class SpedRecordWriterTests
{
    private static string[] Lines(byte[] bytes) =>
        Encoding.Latin1.GetString(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Finish_WritesBlock9WithCountsIncludingItself()
    {
        var writer = new SpedRecordWriter();
        writer.Add("0000", "x");
        writer.Add("0001", 0);
        writer.Add("0990", 3);

        var lines = Lines(writer.Finish());

        Assert.Equal(3 + 7, lines.Length);
        Assert.Equal("|9001|0|", lines[3]);
        Assert.Equal("|9900|0000|1|", lines[4]);
        Assert.Equal("|9900|0001|1|", lines[5]);
        Assert.Equal("|9900|0990|1|", lines[6]);
        Assert.Equal("|9900|9001|1|", lines[7]);
        Assert.Equal("|9900|9900|5|", lines[8]);
        Assert.Equal("|9900|9990|1|", lines[9]);
        Assert.Equal("|9900|9999|1|", lines[10]);
        Assert.Equal("|9990|9|", lines[11]);
        Assert.Equal("|9999|13|", lines[12]);
    }

    [Fact]
    public void Add_FormatsDecimalDateAndNull()
    {
        var writer = new SpedRecordWriter();
        writer.Add("C190", 1234.5m, new DateTime(2026, 1, 31), null, " a|b ", new SpedDecimal(1.5m, 4));

        Assert.Equal(1, writer.CountOf("C190"));
        Assert.Equal(1, writer.CountOfBlock('C'));
        Assert.Equal("|C190|1234,50|31012026||ab|1,5000|", Lines(writer.Finish())[0]);
    }

    [Fact]
    public void Finish_UsesLatin1AndCrlf()
    {
        var writer = new SpedRecordWriter();
        writer.Add("0150", "Ç");
        var bytes = writer.Finish();

        Assert.Contains((byte)0xC7, bytes);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("|0150|Ç|\r\n|9001|", text);
        Assert.EndsWith("\r\n", text);
    }
}
