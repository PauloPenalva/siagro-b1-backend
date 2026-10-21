using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Interfaces;

/// <summary>Regras de distribuição versionadas por vigência (STANDALONE).</summary>
public interface IDistributionRuleService
{
    IQueryable<DistributionRule> QueryAll();
    Task<DistributionRule> CreateAsync(DistributionRule rule);
    Task<DistributionRule?> UpdateHeaderAsync(string code, string name, bool inactive);
    Task<DistributionRuleVersion> SaveVersionAsync(string code, DistributionRuleVersion version, DateTime today);
    Task DeleteVersionAsync(Guid versionId, DateTime today);
}
