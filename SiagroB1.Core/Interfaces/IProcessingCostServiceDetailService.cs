using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;
// ReSharper disable All

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostServiceDetailService : IBaseService<ProcessingCostServiceDetail, int>
    {
        Task<ProcessingCostServiceDetail> CreateAsync(string processingCostKey, ProcessingCostServiceDetail entity);
        Task<ProcessingCostServiceDetail?> FindByKeyAsync(string processingCostKey, int itemId);
        IQueryable<ProcessingCostServiceDetail> GetAllByProcessingCostKey(string processingCostKey);
    }
}