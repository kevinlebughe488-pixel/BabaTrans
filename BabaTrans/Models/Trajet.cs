using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BabaTrans.Models
{
    /// <summary>
    /// Représente un trajet entre deux villes.
    /// </summary>
    public class Trajet
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La ville de départ est obligatoire.")]
        [Display(Name = "Ville de Départ")]
        [StringLength(200)]
        public string VilleDepart { get; set; } = string.Empty;

        [Required(ErrorMessage = "La ville d'arrivée est obligatoire.")]
        [Display(Name = "Ville d'Arrivée")]
        [StringLength(200)]
        public string VilleArrivee { get; set; } = string.Empty;

        [Display(Name = "Distance (km)")]
        [Range(1, 10000, ErrorMessage = "La distance doit être comprise entre 1 et 10 000 km.")]
        public double? DistanceKm { get; set; }

        [Display(Name = "Durée estimée (heures)")]
        [Range(0.5, 500, ErrorMessage = "La durée doit être comprise entre 0,5 et 500 heures.")]
        public double? DureeEstimeeHeures { get; set; }

        // Relations
        [Display(Name = "Moyen de Transport")]
        public int? MoyenTransportId { get; set; }
        [ForeignKey("MoyenTransportId")]
        public virtual MoyenTransport? MoyenTransport { get; set; }

        // Navigation
        public virtual ICollection<Commande> Commandes { get; set; } = new List<Commande>();
    }
}
