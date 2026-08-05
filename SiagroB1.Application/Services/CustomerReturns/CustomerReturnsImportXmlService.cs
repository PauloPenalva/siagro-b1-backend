using System.Globalization;
using System.Xml.Linq;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.CustomerReturns;

/// <summary>
/// Lê o XML da NF-e de devolução emitida pelo cliente e monta o cabeçalho e as linhas, para o
/// operador não redigitar.
///
/// NÃO tenta adivinhar a amarração linha → NF de origem. O layout da NF-e guarda as
/// referências em <c>ide/NFref</c>, que é do CABEÇALHO: o XML não diz qual linha veio de qual
/// origem. Os clientes escrevem isso em texto livre no <c>infAdic/infCpl</c>, que é preservado
/// e exibido na tela — a amarração continua manual, como já é hoje. Casar por quantidade
/// erraria em silêncio, que aqui é o pior tipo de erro.
///
/// Lido com <see cref="XDocument"/> e não com a Zeus.Net.NFe: a biblioteca está referenciada
/// no Infra mas nunca foi exercitada neste projeto, e o valor dela é na EMISSÃO. Para ler uma
/// dezena de campos de um layout estável, a dependência não paga o risco.
/// </summary>
public class CustomerReturnsImportXmlService(IBusinessPartnerService businessPartnerService)
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    public async Task<CustomerReturn> ExecuteAsync(byte[] xmlData, string fileName)
    {
        if (xmlData is null || xmlData.Length == 0)
            throw new DefaultException("Arquivo XML vazio.");

        XDocument document;

        try
        {
            document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(xmlData));
        }
        catch (Exception)
        {
            throw new DefaultException("Arquivo não é um XML válido.");
        }

        // O arquivo pode vir como <nfeProc> (com protocolo) ou <NFe> puro.
        var infNfe = document.Descendants(Nfe + "infNFe").FirstOrDefault()
                     ?? throw new DefaultException(
                         "XML não parece uma NF-e: elemento infNFe não encontrado.");

        var ide = infNfe.Element(Nfe + "ide");
        var emit = infNfe.Element(Nfe + "emit");

        var cnpj = Value(emit, "CNPJ") ?? Value(emit, "CPF");

        var customerReturn = new CustomerReturn
        {
            Key = Guid.NewGuid(),
            // Chave vem no atributo Id como "NFe" + 44 dígitos.
            AccessKey = (infNfe.Attribute("Id")?.Value ?? string.Empty)
                .Replace("NFe", string.Empty, StringComparison.OrdinalIgnoreCase),
            DocumentNumber = Value(ide, "nNF"),
            DocumentSeries = Value(ide, "serie"),
            IssueDate = ParseDate(Value(ide, "dhEmi") ?? Value(ide, "dEmi")),
            TotalValue = ParseDecimal(
                infNfe.Descendants(Nfe + "ICMSTot").FirstOrDefault(), "vNF"),
            TaxPayerComments = Value(infNfe.Element(Nfe + "infAdic"), "infCpl"),
            CardName = Value(emit, "xNome"),
            CardCode = await ResolveCardCodeAsync(cnpj),
            XmlFileName = fileName,
            XmlData = xmlData,
        };

        foreach (var det in infNfe.Elements(Nfe + "det"))
        {
            var prod = det.Element(Nfe + "prod");

            customerReturn.AddItem(new CustomerReturnItem
            {
                Key = Guid.NewGuid(),
                ItemCode = Value(prod, "cProd"),
                ItemName = Value(prod, "xProd"),
                Quantity = ParseDecimal(prod, "qCom"),
                UnitPrice = ParseDecimal(prod, "vUnCom"),
            });
        }

        if (customerReturn.Items.Count == 0)
            throw new DefaultException("XML sem itens (det).");

        return customerReturn;
    }

    /// <summary>
    /// Resolve o cliente pelo CNPJ do emitente. Não encontrar é erro de negócio explícito: sem
    /// cliente não há como listar as notas de origem, e deixar em branco só adiaria a
    /// descoberta para a hora de amarrar.
    /// </summary>
    private async Task<string> ResolveCardCodeAsync(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            throw new DefaultException("XML sem CNPJ/CPF do emitente.");

        var digits = new string(cnpj.Where(char.IsDigit).ToArray());

        var partner = businessPartnerService.QueryAll()
            .FirstOrDefault(p => p.TaxId != null && p.TaxId.Replace(".", "")
                .Replace("/", "").Replace("-", "") == digits);

        return partner?.CardCode
               ?? throw new DefaultException(
                   $"Nenhum parceiro cadastrado com o CNPJ/CPF {cnpj} do emitente do XML.");
    }

    private static string? Value(XElement? parent, string name) =>
        parent?.Element(Nfe + name)?.Value;

    private static decimal ParseDecimal(XElement? parent, string name) =>
        decimal.TryParse(Value(parent, name), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
}
