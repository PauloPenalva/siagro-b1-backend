using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Conferência local da chave de acesso da NF-e do fornecedor (spec terceiro-chave §7). Vale para o documento de
/// terceiro Normal do tipo NF-e, na filial que emite pelo Siagro, ao salvar e ao confirmar, nos dois ambientes —
/// quem pergunta pela regra ativa é o chamador. A consulta à SEFAZ (só Produção) é do
/// <c>SupplierNfeAuthorizationService</c>.
/// </summary>
public static class SupplierNfeKeyGuard
{
    public static bool AppliesTo(PurchaseInvoice invoice) =>
        invoice.IssuerType == DocumentIssuerType.ThirdParty
        && invoice.InvoiceType == PurchaseInvoiceType.Normal
        && invoice.TaxDocumentKind == TaxDocumentKind.Nfe;

    /// <summary>
    /// Recusa a chave incoerente, na ordem da spec. Normaliza a chave colada do DANFE (com espaços) e preenche número
    /// e série em branco a partir dela. <paramref name="supplierTaxId"/>: CNPJ/CPF do fornecedor no cadastro.
    /// </summary>
    public static void Ensure(PurchaseInvoice invoice, string? supplierTaxId)
    {
        var key = string.Concat((invoice.ChaveNFe ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));

        if (key.Length == 0)
            throw new DefaultException("Informe a chave de acesso da NF-e do fornecedor.");

        if (key.Length != 44 || !key.All(char.IsAsciiDigit))
            throw new DefaultException("A chave de acesso tem 44 dígitos.");

        if (CheckDigit(key[..43]) != key[43] - '0')
            throw new DefaultException("Chave de acesso inválida: o dígito verificador não confere.");

        if (key.Substring(20, 2) != "55")
            throw new DefaultException("A chave não é de NF-e (modelo 55).");

        // CPF (produtor rural pessoa física) vem na chave com três zeros à esquerda.
        var supplier = string.Concat((supplierTaxId ?? string.Empty).Where(char.IsAsciiDigit));
        if (supplier.Length == 0 || key.Substring(6, 14) != supplier.PadLeft(14, '0'))
            throw new DefaultException("A chave é de outro emitente: o CNPJ/CPF não é o do fornecedor.");

        var series = key.Substring(22, 3);
        var number = key.Substring(25, 9);

        if (!SameNumber(invoice.TaxDocumentSeries, series) || !SameNumber(invoice.TaxDocumentNumber, number))
            throw new DefaultException("O número/série da chave não conferem com os do documento.");

        invoice.ChaveNFe = key;

        // Sem zeros à esquerda, como a importação grava nNF/serie.
        if (string.IsNullOrWhiteSpace(invoice.TaxDocumentSeries))
            invoice.TaxDocumentSeries = long.Parse(series).ToString();
        if (string.IsNullOrWhiteSpace(invoice.TaxDocumentNumber))
            invoice.TaxDocumentNumber = long.Parse(number).ToString();
    }

    /// <summary>Dígito verificador da chave: módulo 11 com pesos 2 a 9 da direita para a esquerda; resto 0 ou 1 → 0.</summary>
    public static int CheckDigit(string first43)
    {
        var sum = 0;
        var weight = 2;

        for (var i = first43.Length - 1; i >= 0; i--)
        {
            sum += (first43[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    /// <summary>Em branco no documento: será preenchido. Digitado: comparado como número ("001" = "1").</summary>
    private static bool SameNumber(string? typed, string fromKey) =>
        string.IsNullOrWhiteSpace(typed) || (long.TryParse(typed.Trim(), out var value) && value == long.Parse(fromKey));
}
