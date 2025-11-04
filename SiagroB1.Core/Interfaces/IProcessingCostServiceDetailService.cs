using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;
// ReSharper disable All

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostServiceDetailService : IBaseService<ProcessingCostServiceDetail, string>
    {
        Task<ProcessingCostServiceDetail> CreateAsync(string processingCostKey, ProcessingCostServiceDetail entity);
        Task<ProcessingCostServiceDetail?> FindByKeyAsync(string field, string processingCostKey);
        IQueryable<ProcessingCostServiceDetail> GetAllByProcessingCostKey(string processingCostKey);
    }
}