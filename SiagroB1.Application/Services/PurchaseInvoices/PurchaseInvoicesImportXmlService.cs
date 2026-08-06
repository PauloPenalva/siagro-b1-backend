using System.Globalization;
using System.Text;
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
/// A leitura de <c>ide/NFref</c> (que resolve remessa → NF de venda futura) e dos campos fiscais
/// (CFOP, NCM, CST, impostos) entra na Fase 2.
/// </summary>
public class PurchaseInvoicesImportXmlService(IBusinessPartnerService businessPartnerService)
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    /// <summary>
    /// Assíncrono na assinatura, síncrono na execução: a leitura é toda em memória, e o
    /// <c>Task.FromResult</c> evita o aviso de método async sem await. A Fase 2 vai precisar de
    /// consulta ao cadastro de naturezas, e aí o await passa a existir de verdade.
    /// </summary>
    public Task<PurchaseInvoiceDraftDto> ExecuteAsync(byte[] xmlData, string fileName)
    {
        if (xmlData is null || xmlData.Length == 0)
            throw new DefaultException("Arquivo XML vazio.");

        XDocument document;

        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(xmlData));
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

        var draft = new PurchaseInvoiceDraftDto
        {
            // A chave vem no atributo Id como "NFe" + 44 dígitos.
            ChaveNFe = (infNfe.Attribute("Id")?.Value ?? string.Empty)
                .Replace("NFe", string.Empty, StringComparison.OrdinalIgnoreCase),
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

        foreach (var det in infNfe.Elements(Nfe + "det"))
        {
            var prod = det.Element(Nfe + "prod");

            draft.Items.Add(new PurchaseInvoiceDraftItemDto
            {
                ItemCode = Value(prod, "cProd"),
                ItemName = Value(prod, "xProd"),
                UnitOfMeasureCode = Value(prod, "uCom"),
                Quantity = ParseDecimal(prod, "qCom"),
                UnitPrice = ParseDecimal(prod, "vUnCom"),
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
