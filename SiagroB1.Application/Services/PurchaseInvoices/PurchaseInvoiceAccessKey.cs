using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// NF-e referenciada pela entrada própria (spec D7): normalmente a nota do produtor, que vai no
/// <c>ide/NFref/refNFe</c>. Conferida aqui (44 dígitos e dígito verificador mod 11) porque a SEFAZ só a
/// recusaria depois de o número ter sido consumido.
/// </summary>
public static class PurchaseInvoiceAccessKey
{
    public static string? Normalize(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    public static void EnsureValidReference(PurchaseInvoice invoice)
    {
        // Só a entrada própria Normal referencia nota; nos outros casos o campo não tem uso.
        if (invoice.IssuerType != DocumentIssuerType.Own || invoice.InvoiceType != PurchaseInvoiceType.Normal)
        {
            invoice.ReferencedAccessKey = null;
            return;
        }

        invoice.ReferencedAccessKey = Normalize(invoice.ReferencedAccessKey);

        if (invoice.ReferencedAccessKey is null)
            return;

        if (!IsValid(invoice.ReferencedAccessKey) || invoice.ReferencedAccessKey == invoice.ChaveNFe)
            throw new DefaultException("A chave da NF-e referenciada é inválida.");
    }

    private static bool IsValid(string key)
    {
        if (key.Length != 44)
            return false;

        var sum = 0;
        var weight = 2;
        for (var i = 42; i >= 0; i--)
        {
            sum += (key[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var digit = 11 - (sum % 11);
        return (digit >= 10 ? 0 : digit) == key[43] - '0';
    }
}
