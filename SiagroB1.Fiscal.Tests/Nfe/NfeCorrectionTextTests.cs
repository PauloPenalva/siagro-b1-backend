using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// O xCorrecao só aceita Latin-1 visível, com espaços simples no meio (XSD: [!-ÿ]{1}[ -ÿ]{0,}[!-ÿ]{1}).
/// O texto colado do Word chega com aspas curvas, travessão e quebra de linha.
/// </summary>
public class NfeCorrectionTextTests
{
    [Fact]
    public void Line_breaks_tabs_and_repeated_spaces_become_one_space()
    {
        Assert.Equal("Placa correta ABC1D23 no transporte",
            NfeCorrectionText.Normalize("  Placa correta\r\nABC1D23\tno   transporte \n"));
    }

    [Fact]
    public void Typographic_characters_become_ascii()
    {
        Assert.Equal("Onde se le \"X\" - leia-se 'Y'...",
            NfeCorrectionText.Normalize("Onde se le “X” – leia-se ‘Y’…"));
        Assert.Equal("a - b", NfeCorrectionText.Normalize("a — b"));
        Assert.Equal("a b", NfeCorrectionText.Normalize("a b"));
    }

    [Fact]
    public void Null_becomes_empty()
    {
        Assert.Equal(string.Empty, NfeCorrectionText.Normalize(null));
    }

    [Fact]
    public void Accented_latin1_text_is_valid()
    {
        Assert.Empty(NfeCorrectionText.InvalidCharacters("Correção do endereço: Avenida São João, nº 10"));
    }

    [Fact]
    public void Characters_outside_latin1_are_reported_once_each()
    {
        Assert.Equal(['€', '✓'], NfeCorrectionText.InvalidCharacters("Valor € errado ✓ e € de novo"));
    }
}
