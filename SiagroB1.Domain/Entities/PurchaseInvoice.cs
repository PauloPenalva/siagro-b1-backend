using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento fiscal de ENTRADA: NF do fornecedor, compra de mercadoria para comercialização,
/// faturamento antecipado de venda futura do produtor rural e suas remessas, insumo, serviço, e a
/// devolução emitida pelo cliente (<see cref="PurchaseInvoiceType.Return"/>).
///
/// Espelha <see cref="SalesInvoice"/> de propósito: as duas são lidas lado a lado, e manter a mesma
/// forma é o que evita a divergência que a antiga <c>CustomerReturn</c> criou ao virar entidade
/// separada.
///
/// Nesta fase é documento de CONTROLE E CONCILIAÇÃO: não move saldo de contrato, não grava linha de
/// ledger e não toca em romaneio. O efeito de negócio chega na Fase 3, pelo <c>UsageEffect</c> da
/// natureza de operação de cada linha.
/// </summary>
[Table("PURCHASE_INVOICES")]
public class PurchaseInvoice : DocumentEntity
{
    public PurchaseInvoiceType InvoiceType { get; set; } = PurchaseInvoiceType.Normal;

    public DocumentIssuerType IssuerType { get; set; } = DocumentIssuerType.ThirdParty;

    public InvoiceStatus InvoiceStatus { get; set; } = InvoiceStatus.Pending;

    /// <summary>
    /// Número INTERNO do documento. Nulo em documento de terceiro nesta fase; digitado à mão na
    /// emissão própria. A Fase 3 o preenche pelo numerador <c>DocNumbers</c>.
    /// </summary>
    [Column(TypeName = "VARCHAR(9)")]
    public string? InvoiceNumber { get; set; }

    /// <summary>Emitente: fornecedor, produtor ou cliente devolvendo. Sem FK — cadastro dual-mode.</summary>
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Número da nota fiscal, como emitida.</summary>
    [Column(TypeName = "VARCHAR(9)")]
    public string? TaxDocumentNumber { get; set; }

    /// <summary>Série da nota fiscal.</summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string? TaxDocumentSeries { get; set; }

    /// <summary>
    /// Chave da NF-e. Única entre as não canceladas.
    ///
    /// O nome fica em português para casar com <see cref="SalesInvoice.ChaveNFe"/> — exceção
    /// consciente à regra de identificadores em inglês, porque as duas entidades são irmãs diretas
    /// e são lidas juntas o tempo todo.
    /// </summary>
    [Column(TypeName = "VARCHAR(44)")]
    public string? ChaveNFe { get; set; }

    /// <summary>Emissão, como declarada pelo emitente.</summary>
    public DateTime? IssueDate { get; set; } = DateTime.Now.Date;

    /// <summary>Entrada/lançamento na empresa. Pode ser posterior à emissão.</summary>
    public DateTime? PostingDate { get; set; } = DateTime.Now.Date;

    /// <summary>
    /// Total DECLARADO pelo emitente (<c>ICMSTot/vNF</c> do XML).
    ///
    /// Coexiste com <see cref="TotalInvoiceItems"/>, que é a soma das linhas, e divergirem é
    /// INFORMAÇÃO de conciliação — não erro. Frete e impostos entram no declarado e não nas linhas.
    /// É por isso que este campo não pode virar derivado.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal TotalDocumentValue { get; set; }

    /// <summary>
    /// Informações complementares do contribuinte (<c>infAdic/infCpl</c> do XML), guardadas cruas.
    ///
    /// É AQUI que o emitente escreve, em texto livre, as referências que o layout não estrutura por
    /// linha. Exibido na tela: é a cola do operador para fazer a amarração.
    /// </summary>
    [Column(TypeName = "VARCHAR(MAX)")]
    public string? TaxPayerComments { get; set; }

    /// <summary>Observação do cabeçalho. Não confundir com <see cref="CommentEntries"/>.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal NetWeight { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? TruckCode { get; set; }

    [Column(TypeName = "VARCHAR(15)")]
    public string? TruckingCompanyCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? TruckingCompanyName { get; set; }

    public FreightTerms FreightTerms { get; set; }

    /// <summary>
    /// Documento de origem. É como a NF de REMESSA aponta a NF de venda futura que a antecipou —
    /// mesmo mecanismo de <see cref="SalesInvoice.SalesInvoiceOriginKey"/>.
    /// </summary>
    public Guid? PurchaseInvoiceOriginKey { get; set; }
    public virtual PurchaseInvoice? PurchaseInvoiceOrigin { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? XmlFileName { get; set; }

    /// <summary>XML original: prova documental, e permite reprocessar se a leitura mudar.</summary>
    [Column(TypeName = "VARBINARY(MAX)")]
    public byte[]? XmlData { get; set; }

    public ICollection<PurchaseInvoiceItem> Items { get; set; } = [];

    /// <summary>
    /// Comentários do documento: anotações com data, hora e autor, editáveis a qualquer tempo.
    /// Chama-se <c>CommentEntries</c>, e não <c>Comments</c>, porque <see cref="Comments"/> já é o
    /// escalar de "Observações" do cabeçalho.
    /// </summary>
    public ICollection<PurchaseInvoiceComment> CommentEntries { get; set; } = [];

    /// <summary>Log de alterações do documento. Hoje só recebe as linhas de comentário.</summary>
    public ICollection<PurchaseInvoiceChangeLog> ChangeLogs { get; set; } = [];

    [NotMapped]
    public decimal TotalInvoiceItems => Items.Sum(i => i.Total);

    public void AddItem(PurchaseInvoiceItem item)
    {
        item.PurchaseInvoice = this;
        Items.Add(item);
    }
}
