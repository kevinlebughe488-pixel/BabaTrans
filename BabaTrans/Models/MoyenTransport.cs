using System.ComponentModel.DataAnnotations;

namespace BabaTrans.Models
{
    /// <summary>
    /// Représente un moyen de transport utilisé pour acheminer les colis.
    /// </summary>
    public class MoyenTransport
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Le type est obligatoire.")]
        [Display(Name = "Type de Transport")]
        [StringLength(100)]
        public string Type { get; set; } = string.Empty;

        [Display(Name = "Immatriculation")]
        [StringLength(50)]
        public string? Immatriculation { get; set; }

        [Required(ErrorMessage = "La capacité est obligatoire.")]
        [Display(Name = "Capacité (kg)")]
        [Range(1, 100000, ErrorMessage = "La capacité doit être comprise entre 1 et 100 000 kg.")]
        public double Capacite { get; set; }

        [Display(Name = "Disponible")]
        public bool EstDisponible { get; set; } = true;

        [Display(Name = "Description")]
        [StringLength(300)]
        public string? Description { get; set; }

        // Navigation
        public virtual ICollection<Trajet> Trajets { get; set; } = new List<Trajet>();
    }
}
