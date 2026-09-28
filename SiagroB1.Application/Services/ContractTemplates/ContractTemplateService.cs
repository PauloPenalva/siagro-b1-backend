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
