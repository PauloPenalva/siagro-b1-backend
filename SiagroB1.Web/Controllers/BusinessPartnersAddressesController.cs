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
    /// <summary>
    /// Tira as aspas do segmento de chave da URL.
    ///
    /// As rotas daqui são por ATRIBUTO, e não pela convenção do OData: em
    /// <c>BusinessPartners('C90001')/Addresses</c> o <c>{key}</c> captura <c>'C90001'</c>
    /// COM as aspas, e a busca pelo parceiro nunca acha nada. O sintoma era mudo na
    /// leitura (lista de endereços sempre vazia) e um 500 seco na gravação.
    ///
    /// Dentro do <c>$batch</c> o segmento chega ainda codificado (<c>'END%20TESTE'</c>): sem
    /// decodificar, endereço com espaço no nome não era achado. O apóstrofo vem dobrado no literal
    /// do OData (<c>'D''AGUA'</c>).
    /// </summary>
    private static string Unquote(string key)
    {
        var value = Uri.UnescapeDataString(key);

        // Uma aspa de cada ponta, não Trim: nome que termina em apóstrofo ('OLHO D''') o perdia.
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            value = value[1..^1];

        return value.Replace("''", "'");
    }

    [HttpPost("odata/BusinessPartners({key})/Addresses")]
    public async Task<ActionResult<AddressModel>> PostAsync(
        [FromRoute] string key, 
        [FromBody] AddressModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        // A tela não manda o CardCode no corpo (vem da URL). É chave não anulável no EDM: sem ele
        // a resposta estourava na serialização depois de gravar — 500 dentro do $batch do /edit.
        model.CardCode = Unquote(key);

        try
        {
            await service.Create(model.CardCode, model);

            // Created monta o Location a partir do entity set, e esta rota de atributo não tem
            // entity set (EdmUnknownEntitySet): o endereço gravava e a resposta estourava 500
            // com InvalidCastException. Ok devolve o mesmo corpo sem precisar do Location.
            return Ok(model);
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
            await service.Update(Unquote(key), Unquote(addressName), Unquote(adresType), model);
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
            await service.Delete(Unquote(key), Unquote(addressName), Unquote(adresType));
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
        return Ok(service.QueryAll(Unquote(key)));
    }
    
    [HttpGet("odata/BusinessPartners({key})/Addresses(AddressName={addressName},AdresType={adresType},CardCode={cardCode})")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractBroker>> GetAsync(
        [FromRoute] string key, 
        [FromRoute] string addressName,
        [FromRoute] string adresType,
        [FromRoute] string cardCode)
    {
        var item = await service.GetByIdAsync(Unquote(key), Unquote(addressName), Unquote(adresType));

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

        var t = await service.GetByIdAsync(Unquote(key), Unquote(addressName), Unquote(adresType));

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await service.Update(Unquote(key), Unquote(addressName), Unquote(adresType), t);
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