using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostDryingDetailService : IBaseService<ProcessingCostDryingDetail, Guid>
    {
        Task<ProcessingCostDryingDetail> CreateAsync(Guid processingCostKey, ProcessingCostDryingDetail entity);
        Task<ProcessingCostDryingDetail?> FindByKeyAsync(Guid processingCostKey, Guid itemId);
        IQueryable<ProcessingCostDryingDetail> GetAllByProcessingCostKey(Guid processingCostKey);
    }
}