using System.Globalization;
using System.Xml.Linq;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Lê o XML da NF-e de entrada e monta o rascunho do cabeçalho e das linhas, para o operador não
/// redigitar. SÓ LÊ — quem grava é o POST do documento.
///
/// NÃO tenta adivinhar a amarração linha → NF de origem. O layout da NF-e guarda as referências em
/// <c>ide/NFref</c>, que é do CABEÇALHO: o XML não diz qual linha veio de qual origem. Os emitentes
/// escrevem isso em texto livre no <c>infAdic/infCpl</c>, que é preservado e exibido na tela — a
/// amarração continua manual. Casar por quantidade erraria em silêncio, que aqui é o pior tipo de
/// erro.
///
/// Lido com <see cref="XDocument"/> e não com a Zeus.Net.NFe: a biblioteca está referenciada no
/// Infra mas nunca foi exercitada neste projeto, e o valor dela é na EMISSÃO. Para ler uma dezena
/// de campos de um layout estável, a dependência não paga o risco.
///
/// A tributação de cada item é lida por <see cref="SupplierNfeXmlReader"/> e gravada na linha pelo
/// <c>PurchaseInvoiceSupplierTaxes</c> quando o documento é salvo; o rascunho só leva o <c>nItem</c>.
/// </summary>
public class PurchaseInvoicesImportXmlService(IBusinessPartnerService businessPartnerService)
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    /// <summary>
    /// Assíncrono na assinatura, síncrono na execução: a leitura é toda em memória, e o
    /// <c>Task.FromResult</c> evita o aviso de método async sem await.
    /// </summary>
    public Task<PurchaseInvoiceDraftDto> ExecuteAsync(byte[] xmlData, string fileName)
    {
        var nfe = SupplierNfeXmlReader.Read(xmlData);
        var infNfe = nfe.InfNfe;
        var ide = infNfe.Element(Nfe + "ide");
        var emit = infNfe.Element(Nfe + "emit");
        var cnpj = Value(emit, "CNPJ") ?? Value(emit, "CPF");

        var draft = new PurchaseInvoiceDraftDto
        {
            ChaveNFe = nfe.AccessKey,
            TaxDocumentNumber = Value(ide, "nNF"),
            TaxDocumentSeries = Value(ide, "serie"),
            IssueDate = ParseDate(Value(ide, "dhEmi") ?? Value(ide, "dEmi")),
            TotalDocumentValue = ParseDecimal(
                infNfe.Descendants(Nfe + "ICMSTot").FirstOrDefault(), "vNF"),
            TaxPayerComments = Value(infNfe.Element(Nfe + "infAdic"), "infCpl"),
            CardName = Value(emit, "xNome"),
            CardCode = ResolveCardCode(cnpj),
            XmlFileName = fileName,
        };

        foreach (var item in nfe.Items)
        {
            draft.Items.Add(new PurchaseInvoiceDraftItemDto
            {
                ItemCode = item.ProductCode,
                ItemName = item.ProductName,
                UnitOfMeasureCode = item.UnitOfMeasure,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                NfeItemNumber = item.ItemNumber,
            });
        }

        if (draft.Items.Count == 0)
            throw new DefaultException("XML sem itens (det).");

        return Task.FromResult(draft);
    }

    /// <summary>
    /// Resolve o emitente pelo CNPJ/CPF. Não encontrar é erro de negócio EXPLÍCITO: sem parceiro
    /// não há como listar as notas de origem, e deixar em branco só adiaria a descoberta para a
    /// hora de amarrar.
    /// </summary>
    private string ResolveCardCode(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            throw new DefaultException("XML sem CNPJ/CPF do emitente.");

        var digits = Digits(cnpj);

        var partner = businessPartnerService.QueryAll()
            .FirstOrDefault(p => p.TaxId != null && Digits(p.TaxId) == digits);

        return partner?.CardCode
               ?? throw new DefaultException(
                   $"Nenhum parceiro cadastrado com o CNPJ/CPF {cnpj} do emitente do XML.");
    }

    /// <summary>Compara só os dígitos: o cadastro guarda com máscara e o XML sem.</summary>
    private static string Digits(string value) =>
        new(value.Where(char.IsDigit).ToArray());

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
