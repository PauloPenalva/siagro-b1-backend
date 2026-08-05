using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// NF-e de devolução emitida pelo CLIENTE contra a Yokotobi — tipicamente a quebra de peso
/// apurada no destino, no fechamento do contrato.
///
/// É documento de CONTROLE E CONCILIAÇÃO: não move saldo de contrato, não gera linha de ledger
/// e não mexe em romaneio. O movimento físico já foi registrado na Conferência de Entrega, e o
/// fator efetivo de <c>SalesContractsRecalculateBalanceService</c> já devolveu o volume ao
/// contrato — esta nota é o documento FISCAL do mesmo fato. Fazê-la restaurar saldo creditaria
/// o contrato duas vezes.
///
/// Não confundir com <see cref="SalesInvoiceType.Return"/>: aquele é para mercadoria que VOLTA
/// fisicamente, e reverte os romaneios. Aqui não voltou grão nenhum.
///
/// A escrituração fiscal da entrada (livro, SPED, apuração) continua no ERP do cliente.
/// </summary>
[Table("CUSTOMER_RETURNS")]
public class CustomerReturn : BaseEntity
{
    /// <summary>Emitente da nota: o cliente. Sem FK — cadastro dual-mode.</summary>
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    [Column(TypeName = "VARCHAR(9)")]
    public string? DocumentNumber { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string? DocumentSeries { get; set; }

    /// <summary>Chave da NF-e do cliente. Única entre as não canceladas.</summary>
    [Column(TypeName = "VARCHAR(44)")]
    public string? AccessKey { get; set; }

    public DateTime? IssueDate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal TotalValue { get; set; }

    /// <summary>
    /// Informações complementares do contribuinte (<c>infAdic/infCpl</c> do XML).
    ///
    /// É AQUI que o cliente escreve, em texto livre, número/série/chave e quantidade das notas
    /// de origem — o layout da NF-e só guarda as referências no cabeçalho (<c>ide/NFref</c>) e
    /// não diz qual linha veio de qual origem. Guardado cru e exibido na tela: é a cola do
    /// operador para fazer a amarração.
    /// </summary>
    [Column(TypeName = "VARCHAR(MAX)")]
    public string? TaxPayerComments { get; set; }

    public CustomerReturnStatus Status { get; set; } = CustomerReturnStatus.Registered;

    [Column(TypeName = "VARCHAR(200)")]
    public string? XmlFileName { get; set; }

    /// <summary>
    /// XML original, como <see cref="SalesContractAttachment.FileData"/> já faz. É a prova
    /// documental e permite reprocessar se a leitura mudar.
    /// </summary>
    [Column(TypeName = "VARBINARY(MAX)")]
    public byte[]? XmlData { get; set; }

    public ICollection<CustomerReturnItem> Items { get; set; } = [];

    public void AddItem(CustomerReturnItem item)
    {
        item.CustomerReturn = this;
        Items.Add(item);
    }
}
