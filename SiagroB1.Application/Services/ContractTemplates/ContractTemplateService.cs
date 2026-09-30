using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractTemplates;

/// <summary>
/// CRUD do modelo de contrato. A única regra é a validação dos placeholders no salvamento: o
/// erro aparece na edição, e não semanas depois na hora de gerar a minuta.
/// </summary>
public class ContractTemplateService(AppDbContext context, ILogger<ContractTemplateService> logger)
    : BaseService<ContractTemplate, Guid>(context, logger)
{
    public override Task<ContractTemplate> CreateAsync(ContractTemplate entity)
    {
        Validate(entity);
        entity.CreatedAt = DateTime.Now;
        entity.UpdatedAt = DateTime.Now;
        return base.CreateAsync(entity);
    }

    public override Task<ContractTemplate?> UpdateAsync(Guid key, ContractTemplate entity)
    {
        Validate(entity);
        entity.UpdatedAt = DateTime.Now;
        return base.UpdateAsync(key, entity);
    }

    /// <summary>
    /// Modelo que já originou minuta não é excluído.
    ///
    /// A FK CONTRACT_DRAFTS → CONTRACT_TEMPLATES é <c>NoAction</c>, então o banco recusaria de
    /// qualquer jeito — mas o <see cref="BaseService{T,TId}.DeleteAsync"/> engole a exceção e
    /// relança <c>DefaultException("Error deleting entity.")</c>: o usuário receberia 400 com uma
    /// frase em inglês que não diz o motivo nem o que fazer. Aqui a recusa vem antes, em pt-BR,
    /// dizendo quantas minutas seguram o modelo e apontando a saída (inativar).
    ///
    /// <see cref="DefaultException"/> e não <see cref="BusinessException"/> de propósito: o
    /// <c>ODataBaseController</c> só mapeia a primeira para <c>BadRequest</c>; a segunda vira 500.
    /// </summary>
    public override async Task<bool> DeleteAsync(Guid key)
    {
        var drafts = await context.ContractDrafts.CountAsync(d => d.TemplateKey == key);

        if (drafts > 0)
            throw new DefaultException(
                $"O modelo não pode ser excluído: {drafts} minuta(s) já foram geradas a partir dele. " +
                "Para não oferecê-lo em minutas novas, desmarque \"Ativo\".");

        return await base.DeleteAsync(key);
    }

    /// <summary>Lança <see cref="BusinessException"/> com os nomes desconhecidos; nada é gravado.</summary>
    public static void Validate(ContractTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.BodyHtml))
            throw new BusinessException("O texto do modelo não pode ficar vazio.");

        var unknown = ContractDraftTemplateRenderer.FindUnknown(
            template.BodyHtml, ContractDraftPlaceholderCatalog.NamesFor(template.ContractType));

        if (unknown.Count > 0)
            throw new BusinessException(ContractDraftTemplateRenderer.UnknownPlaceholdersMessage(unknown));
    }
}
