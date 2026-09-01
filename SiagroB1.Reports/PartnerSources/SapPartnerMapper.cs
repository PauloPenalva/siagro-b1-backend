using System.Globalization;
using System.Text;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Reports.PartnerSources;

/// <summary>
/// Regras puras de leitura do parceiro no SAP Business One, isoladas do EF para
/// poderem ser testadas: escolha do endereço fiscal (CRD7), montagem do endereço
/// completo (CRD1 + OCNT) e leitura das pessoas de contato (OCPR). Espelham as
/// queries e os critérios homologados pelo cliente.
/// </summary>
public static class SapPartnerMapper
{
    /// <summary>
    /// Escolhe a linha de CRD7 de onde saem CNPJ, CPF e IE. A prioridade evita que o
    /// documento saia em branco quando o endereço padrão do parceiro não o tem:
    /// 1) endereço padrão COM documento, 2) qualquer endereço com documento,
    /// 3) endereço padrão, 4) o primeiro por nome.
    /// </summary>
    public static AddressTaxExtension? SelectFiscalAddress(
        IEnumerable<AddressTaxExtension> taxExtensions, string? shipToDef)
        => taxExtensions
            .OrderBy(x => Rank(x, shipToDef))
            .ThenBy(x => x.AddressName, StringComparer.Ordinal)
            .FirstOrDefault();

    private static int Rank(AddressTaxExtension tax, string? shipToDef)
    {
        var isDefault = shipToDef is not null && tax.AddressName == shipToDef;
        var hasDocument = !string.IsNullOrWhiteSpace(tax.Cnpj)
                          || !string.IsNullOrWhiteSpace(tax.Cpf);

        return (isDefault, hasDocument) switch
        {
            (true, true) => 0,
            (false, true) => 1,
            (true, false) => 2,
            _ => 3
        };
    }

    /// <summary>
    /// Monta o endereço em uma linha, em maiúsculas, omitindo os trechos vazios.
    /// O nome do município vem de OCNT quando existir; a UF de CRD1 tem precedência
    /// sobre a de OCNT.
    /// </summary>
    public static string? BuildFullAddress(Address? address, County? county)
    {
        if (address is null) return null;

        var street = Join(" ", address.StreetType, address.Street);

        var parts = new (string Prefix, string? Value)[]
        {
            ("", street),
            (", ", address.StreetNo),
            (" - BAIRRO: ", address.Block),
            (" - MUNICÍPIO: ", Coalesce(county?.Name, address.City)),
            (" - UF: ", Coalesce(address.State, county?.State)),
            (" - CEP: ", address.ZipCode)
        };

        var text = string.Concat(parts
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select((p, index) => (index == 0 ? "" : p.Prefix) + p.Value!.Trim()));

        return string.IsNullOrWhiteSpace(text) ? null : text.ToUpperInvariant();
    }

    /// <summary>
    /// Sócios administradores em uma linha: <c>NOME (CPF); NOME (CPF)</c>. Sem CPF,
    /// sai só o nome. O CPF vem de <c>OCPR.Notes1</c>.
    /// </summary>
    public static string? BuildManagingPartners(IEnumerable<ContactPerson> contacts)
    {
        var partners = contacts
            .Where(c => PositionMatches(c.Position, "SOCIO"))
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => string.IsNullOrWhiteSpace(c.Notes1)
                ? c.Name!.Trim()
                : $"{c.Name!.Trim()} ({c.Notes1.Trim()})")
            .ToList();

        return partners.Count == 0 ? null : string.Join("; ", partners);
    }

    /// <summary>
    /// Contato para envio do contrato: e-mail e celular do primeiro contato cujo cargo
    /// case com "Contrato". Sem celular, cai no telefone fixo.
    /// </summary>
    public static string? BuildContractContact(IEnumerable<ContactPerson> contacts)
    {
        var contact = contacts.FirstOrDefault(c => PositionMatches(c.Position, "CONTRATO"));

        if (contact is null) return null;

        var channels = new[] { contact.Email, Coalesce(contact.MobilePhone, contact.Phone) }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToList();

        return channels.Count == 0 ? null : string.Join(" / ", channels);
    }

    /// <summary>
    /// Compara o cargo digitado à mão com o classificador esperado, ignorando caixa,
    /// acentos e espaços em volta. "Sócio", "socio " e "Sócios Administradores" casam
    /// todos com "SOCIO".
    /// </summary>
    public static bool PositionMatches(string? position, string expected)
        => !string.IsNullOrWhiteSpace(position)
           && RemoveDiacritics(position).Trim().ToUpperInvariant()
               .Contains(RemoveDiacritics(expected).ToUpperInvariant());

    private static string RemoveDiacritics(string text)
        => new(text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

    private static string? Coalesce(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    private static string? Join(string separator, params string?[] values)
        => string.Join(separator, values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim()));
}
