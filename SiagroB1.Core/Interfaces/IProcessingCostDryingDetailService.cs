using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostDryingDetailService : IBaseService<ProcessingCostDryingDetail, int>
    {
        Task<ProcessingCostDryingDetail> CreateAsync(string processingCostKey, ProcessingCostDryingDetail entity);
        Task<ProcessingCostDryingDetail?> FindByKeyAsync(string processingCostKey, int itemId);
        IQueryable<ProcessingCostDryingDetail> GetAllByProcessingCostKey(string processingCostKey);
    }
}