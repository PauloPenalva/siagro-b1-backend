using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Domain.Entities;

[Table("storage_lots")]
public class StorageLot : BaseEntity<string>
{
    [Column("date")]
    public required DateOnly Date { get; set; }

    [Column("description")]
    public required string Description { get; set; }

    [Column("card_code")]
    [ForeignKey("BusinessPartner")]
    public required string CardCode { get; set; }
    public virtual BusinessPartner? BusinessPartner { get; set; }
    
    [Column("item_code")]
    [ForeignKey("Item")]
    public required string ItemCode { get; set; }
    public virtual Item? Item { get; set; }

    [Column("processing_cost_key")]
    [ForeignKey("ProcessingCost")]
    public required string ProcessingCostKey { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }
    
    [Column("whare_house_key")]
    [ForeignKey("WhareHouse")]
    public required string WhareHouseKey { get; set; }
    public virtual WhareHouse? WhareHouse { get; set; }
    
    [Column("balance")]
    public decimal Balance { get; set; } = 0;
}