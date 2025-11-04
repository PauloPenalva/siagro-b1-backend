using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostDryingDetailService : IBaseService<ProcessingCostDryingDetail, string>
    {
        Task<ProcessingCostDryingDetail> CreateAsync(string processingCostKey, ProcessingCostDryingDetail entity);
        Task<ProcessingCostDryingDetail?> FindByKeyAsync(string processingCostKey, string key);
        IQueryable<ProcessingCostDryingDetail> GetAllByProcessingCostKey(string processingCostKey);
    }
}