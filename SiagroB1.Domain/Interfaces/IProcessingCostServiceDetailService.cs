using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Interfaces;

public interface IProcessingCostServiceDetailService : IBaseService<ProcessingCostServiceDetail, string>
{
    Task<ProcessingCostServiceDetail> CreateAsync(string processingCostCode, ProcessingCostServiceDetail entity);
    Task<ProcessingCostServiceDetail?> FindByKeyAsync(string processingCostCode, int itemId);
    IQueryable<ProcessingCostServiceDetail> GetAllByProcessingCostKey(string processingCostCode);
}
