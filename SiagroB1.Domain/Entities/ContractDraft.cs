using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Minuta: fotografia do contrato no momento da criação (<see cref="BodyHtml"/> renderizado e
/// <see cref="PlaceholdersJson"/> com os valores que entraram). Se o contrato mudar depois, a
/// minuta não muda — o usuário cria outra. Uma tabela para compra e venda: exatamente uma das
/// duas FKs é preenchida (check constraint <c>CK_CONTRACT_DRAFTS_ONE_CONTRACT</c>).
/// </summary>
[Table("CONTRACT_DRAFTS")]
[Index(nameof(PurchaseContractKey))]
[Index(nameof(SalesContractKey))]
public class ContractDraft : DocumentEntity
{
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>Snapshot do código do contrato, para listar sem join.</summary>
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ContractCode { get; set; }

    /// <summary>1, 2, 3… por contrato. A tela exibe "Minuta {Sequence}".</summary>
    public int Sequence { get; set; }

    public Guid TemplateKey { get; set; }
    public virtual ContractTemplate? Template { get; set; }

    public ContractDraftType DraftType { get; set; } = ContractDraftType.Contract;

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(MAX) NOT NULL")]
    public required string BodyHtml { get; set; }

    [Column(TypeName = "VARCHAR(MAX) NOT NULL")]
    public required string PlaceholdersJson { get; set; }

    public ContractDraftStatus Status { get; set; } = ContractDraftStatus.Draft;

    // ---- preenchidos pela Fase 2 (assinatura eletrônica) ----

    [Column(TypeName = "VARCHAR(20)")]
    public string? Provider { get; set; }

    /// <summary>uuid do documento no provedor; único porque o webhook procura por ele.</summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? ExternalDocumentId { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? SignedAt { get; set; }

    [Column(TypeName = "VARCHAR(1000)")]
    public string? LastError { get; set; }

    /// <summary>Anexo do contrato onde o PDF assinado foi guardado.</summary>
    public Guid? SignedAttachmentKey { get; set; }

    /// <summary>
    /// Última vez que o job de reconciliação OLHOU esta minuta, tenha mudado algo ou não. É o
    /// relógio da varredura, separado do <c>UpdatedAt</c> de propósito: o <c>UpdatedAt</c> é
    /// auditoria que o usuário lê, e o job grava com o nome "reconciliacao" — carimbá-lo a cada
    /// consulta silenciosa apagaria quem de fato mexeu na minuta. Sem este campo, uma minuta que
    /// ninguém assina nunca muda de <c>UpdatedAt</c>, volta a ser a mais antiga a cada 30 min e,
    /// passando de 50 nessa situação, o lote fica preso nelas e minuta nova nenhuma é conferida.
    /// Nulo nas linhas anteriores à sua criação — a consulta cai no <c>UpdatedAt</c> nesse caso,
    /// para não considerar "recém-conferido" tudo o que já existia.
    /// </summary>
    public DateTime? LastCheckedAt { get; set; }

    public virtual ICollection<ContractDraftSigner> Signers { get; set; } = [];

    [NotMapped]
    public bool IsPurchase => PurchaseContractKey.HasValue;
}
