using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Actions.StorageAddresses;

public class StorageAddressesJobsController(
    IStorageAddressesDailyCalculationJob storageDailyCalculationJob,
    IStringLocalizer<Resource> resource
    ) : ODataController
{

    [HttpPost("odata/StorageAddressesDailyCalculationJob")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
           var processingDate = parameters["ProcessingDate"] == null 
               ? DateTime.Today.AddDays(-1) 
               : Convert.ToDateTime(parameters["ProcessingDate"]);
            
           await storageDailyCalculationJob.ExecuteAsync(processingDate);
            
           return Ok(new
           {
               Message = resource["MESSAGE_00004"].Value,
               ProcessingDate = processingDate.Date
           });
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
    
}