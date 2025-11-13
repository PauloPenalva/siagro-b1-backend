using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;
// ReSharper disable All

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostServiceDetailService : IBaseService<ProcessingCostServiceDetail, Guid>
    {
        Task<ProcessingCostServiceDetail> CreateAsync(Guid processingCostKey, ProcessingCostServiceDetail entity);
        Task<ProcessingCostServiceDetail?> FindByKeyAsync(Guid processingCostKey, Guid key);
        IQueryable<ProcessingCostServiceDetail> GetAllByProcessingCostKey(Guid processingCostKey);
    }
}