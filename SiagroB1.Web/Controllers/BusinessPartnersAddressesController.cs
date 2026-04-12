using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;

namespace SiagroB1.Web.Controllers;

public class BusinessPartnersAddressesController(
    IBusinessPartnerAddressService service
    ) 
    : ODataController
{
    [HttpPost("odata/BusinessPartners({key})/Addresses")]
    public async Task<ActionResult<AddressModel>> PostAsync(
        [FromRoute] string key, 
        [FromBody] AddressModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            await service.Create(key, model);

            return Created(model);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }

    [HttpPut("odata/BusinessPartners({key})/Addresses(AddressName={addressName},AdresType={adresType},CardCode={cardCode})")]
    public async Task<IActionResult> PutAsync(
        [FromRoute] string key, 
        [FromRoute] string addressName,
        [FromRoute] string adresType,
        [FromRoute] string cardCode,
        [FromBody] AddressModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await service.Update(key, addressName, adresType, model);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
    
    [HttpDelete("odata/BusinessPartners({key})/Addresses(AddressName={addressName},AdresType={adresType},CardCode={cardCode})")]
    public async Task<IActionResult> DeleteAsync(
        [FromRoute] string key, 
        [FromRoute] string addressName, 
        [FromRoute] string adresType,
        [FromRoute] string cardCode)
    {
        try
        {
            await service.Delete(key, addressName, adresType);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
    

    [HttpGet("odata/BusinessPartners({key})/Addresses")]
    [HttpGet("odata/BusinessPartners/{key}/Addresses")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractBroker>> GetAsync([FromRoute] string key)
    {
        return Ok(service.QueryAll(key));
    }
    
    [HttpGet("odata/BusinessPartners({key})/Addresses(AddressName={addressName},AdresType={adresType},CardCode={cardCode})")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractBroker>> GetAsync(
        [FromRoute] string key, 
        [FromRoute] string addressName,
        [FromRoute] string adresType,
        [FromRoute] string cardCode)
    {
        var item = await service.GetByIdAsync(key,addressName, adresType);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch(
        [FromRoute] string key, 
        [FromRoute] string addressName,
        [FromRoute] string adresType,
        [FromRoute] string cardCode,
        [FromBody] Delta<AddressModel> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await service.GetByIdAsync(key, addressName, adresType);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await service.Update(key, addressName, adresType, t);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}