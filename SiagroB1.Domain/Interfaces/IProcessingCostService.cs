using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Interfaces;

public interface IProcessingCostService : IBaseService<ProcessingCost, string>
{
    Task<ProcessingCostServiceDetail?> FindTabelaCustoServicoById(string processingCostCode, string serviceDetailId);
    Task<ProcessingCostServiceDetail?> UpdateTabelaCustoServicoAsync(string processingCostCode, ProcessingCostServiceDetail entity);
    IQueryable<ProcessingCostDryingParameter> GetDescontosSecagem(string code);
    IQueryable<ProcessingCostDryingDetail> GetValoresSecagem(string code);
    IQueryable<ProcessingCostServiceDetail> GetServicos(string code);
    IQueryable<ProcessingCostQualityParameter> GetQualidades(string code);
    Task<ProcessingCostDryingParameter> CreateDescontosSecagemAsync(string code, ProcessingCostDryingParameter t);
}
