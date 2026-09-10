using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES")]
public class SalesInvoice : DocumentEntity
{
    [Column(TypeName = "VARCHAR(9)")] 
    public string? InvoiceNumber { get; set; } = string.Empty;

    public DateTime? InvoiceDate { get; set; } = DateTime.Now.Date;

    public InvoiceStatus? InvoiceStatus { get; set; }
    
    public SalesInvoiceType  InvoiceType { get; set; } = SalesInvoiceType.Normal;
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; } = string.Empty;  
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal NetWeight  { get; set; }
    
    [Column(TypeName = "VARCHAR(15)")]
    public string? DeliveryCardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? DeliveryCardName { get; set; } = string.Empty; 
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public string? TruckingCompanyCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string? TruckingCompanyName { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; } = string.Empty;
    
    public FreightTerms FreightTerms { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FreightCostStandard { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    /// <summary>
    /// Numero da Nota Fiscal
    /// </summary>
    [Column(TypeName = "VARCHAR(9)")]
    public string? TaxDocumentNumber  { get; set; }
    
    /// <summary>
    /// Serie da Nota Fiscal
    /// </summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string? TaxDocumentSeries { get; set; }

    /// <summary>
    /// Chave da NF-e
    /// </summary>
    [Column(TypeName = "VARCHAR(44)")]
    public string? ChaveNFe { get; set; }
    
    /// <summary>
    /// Informações do Contribuinte
    /// </summary>
    [Column(TypeName = "VARCHAR(500) DEFAULT ''")]
    public string? TaxPayerComments { get; set; }
    
    /// <summary>
    /// Informações do Interesse do Fisco
    /// </summary>
    [Column(TypeName = "VARCHAR(500) DEFAULT ''")]
    public string? TaxComments { get; set; }
    
    public SalesInvoiceDeliveryStatus DeliveryStatus { get; set; } = SalesInvoiceDeliveryStatus.Open;
    
    public DateTime? DeliveryDate { get; set; }
    
    public ICollection<SalesInvoiceItem> Items { get; set; } = [];

    public ICollection<StorageTransaction> SalesTransactions { get; set; } = [];

    /// <summary>
    /// Comentários do documento: anotações com data, hora e autor, editáveis a qualquer tempo.
    /// Chama-se <c>CommentEntries</c>, e não <c>Comments</c>, porque <see cref="Comments"/> já é o
    /// escalar de "Observações" do cabeçalho.
    /// </summary>
    public ICollection<SalesInvoiceComment> CommentEntries { get; set; } = [];

    /// <summary>
    /// Log de alterações do documento. Hoje só recebe as linhas de comentário.
    /// </summary>
    public ICollection<SalesInvoiceChangeLog> ChangeLogs { get; set; } = [];
    
    /// <summary>
    /// Carga que este documento consome. No cabeçalho porque uma nota consome de UMA carga.
    /// A nulidade é o discriminador de fluxo: nula = documento legado (por romaneio solto) ou
    /// avulso; preenchida = documento de carga, que não escreve SalesTransactions.
    /// </summary>
    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public Guid? SalesInvoiceOriginKey { get; set; }
    public SalesInvoice? SalesInvoiceOrigin { get; set; }
    
    [NotMapped]
    public decimal TotalInvoiceItems => Items.Sum(i => i.Total);

    /// <summary>
    /// Total de impostos do documento, somado das linhas. Derivado, sem coluna persistida —
    /// mesmo padrão de <see cref="TotalInvoiceItems"/>.
    /// </summary>
    [NotMapped]
    public decimal TotalInvoiceTaxes => Items.Sum(i => i.TotalTaxes);
    

    /// <summary>
    /// Contrato(s) de venda das linhas do documento. Derivado, sem coluna persistida — depende
    /// de <see cref="Items"/> (e do contrato de cada linha) estar carregado: só as consultas
    /// que fazem o <c>Include</c> devolvem valor, como já acontece com
    /// <see cref="TotalInvoiceItems"/>. Nota de carga tem uma única linha e portanto um único
    /// contrato; a junção existe para os documentos avulsos, que podem ter mais de um.
    /// </summary>
    [NotMapped]
    public string? SalesContractCode => JoinDistinct(DistinctContracts().Select(c => c.Code));

    /// <summary>
    /// Complemento do(s) contrato(s) de venda das linhas. Mesmo padrão de
    /// <see cref="SalesContractCode"/>.
    /// </summary>
    [NotMapped]
    public string? SalesContractComplement => JoinDistinct(DistinctContracts().Select(c => c.Complement));

    /// <summary>
    /// Frete standard do contrato de venda do documento. Só tem significado quando o documento
    /// aponta para UM contrato — com contratos diferentes nas linhas não existe um valor único
    /// e a propriedade devolve <c>null</c> em vez de escolher um deles.
    /// </summary>
    [NotMapped]
    public decimal? SalesContractFreightCostStandard
    {
        get
        {
            var contracts = DistinctContracts();
            return contracts.Count == 1 ? contracts[0].FreightCostStandard : null;
        }
    }

    /// <summary>Contratos de venda distintos referenciados pelas linhas, na ordem das linhas.</summary>
    private List<SalesContract> DistinctContracts() =>
        Items.Select(i => i.SalesContract)
            .Where(c => c != null)
            .Select(c => c!)
            .DistinctBy(c => c.Key)
            .ToList();

    private static string? JoinDistinct(IEnumerable<string?> values)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count == 0 ? null : string.Join(" / ", distinct);
    }

    public void AddItem(SalesInvoiceItem item)
    {
        item.SalesInvoice = this;
        Items.Add(item);
    }
}
