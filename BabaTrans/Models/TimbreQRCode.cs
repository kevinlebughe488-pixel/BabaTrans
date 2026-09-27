using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BabaTrans.Models
{
    /// <summary>
    /// Représente le timbre QR-Code associé à un colis.
    /// </summary>
    public class TimbreQRCode
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Contenu du QR-Code")]
        public string Contenu { get; set; } = string.Empty;

        [Display(Name = "Image QR-Code (Base64)")]
        public string? ImageBase64 { get; set; }

        [Display(Name = "Date de Génération")]
        public DateTime DateGeneration { get; set; } = DateTime.Now;

        // Relations
        [Required]
        public int ColisId { get; set; }
        [ForeignKey("ColisId")]
        public virtual Colis? Colis { get; set; }
    }
}
