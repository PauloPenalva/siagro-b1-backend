using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Modelo de contrato: um único texto HTML com <c>{{placeholders}}</c>, editado pelo usuário na
/// tela. Não há cláusulas em tabela separada — a Tagui tentou e removeu no release 7.8.
/// Sem BranchCode: é documentação da empresa, não documento de filial.
/// </summary>
[Table("CONTRACT_TEMPLATES")]
[Index(nameof(Name), IsUnique = true)]
public class ContractTemplate : BaseEntity
{
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Título do documento; vira o nome do arquivo no provedor de assinatura.</summary>
    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Title { get; set; }

    public ContractTemplateScope ContractType { get; set; } = ContractTemplateScope.Both;

    [Column(TypeName = "VARCHAR(MAX) NOT NULL")]
    public required string BodyHtml { get; set; }

    /// <summary>Inativo não aparece para criar minuta; minutas antigas continuam apontando para ele.</summary>
    public bool Active { get; set; } = true;
}
