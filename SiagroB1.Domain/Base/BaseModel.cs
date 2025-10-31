using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Base
{
    public abstract class BaseEntity<ID>
    {
        [Key]
        [Column(name: "id", Order = 1)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ID Id { get; set; }

        [Column(name: "filial_id", Order = 2)]
        public int? FilialId { get; set; }

        [ForeignKey("FilialId")]
        public Filial? Filial { get; set; }

        [Column(name: "chave_integracao", Order = 3)]
        [JsonPropertyOrder(2)]
        [Display(Name = "Codigo ERP")]
        public string? ChaveIntegracao { get; set; }

        // public DateTime CreatedAt { get; set; } = DateTime.Now;

        // public DateTime? UpdatedAt { get; set; }

        // public DateTime? DeletedAt { get; set; }

        // public bool IsDeleted { get; set; } = false;

        // public void MarkAsDeleted()
        // {
        //     IsDeleted = true;
        //     DeletedAt = DateTime.Now;
        // }

        // public void MarkAsNotDeleted()
        // {
        //     IsDeleted = false;
        //     DeletedAt = null;
        // }

        // public void MarkAsCreated()
        // {
        //     CreatedAt = DateTime.Now;
        // }

        // public void MarkAsUpdated()
        // {
        //     UpdatedAt = DateTime.Now;
        // }
    }
}