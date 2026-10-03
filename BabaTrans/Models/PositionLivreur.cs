using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BabaTrans.Models
{
    /// <summary>
    /// Position GPS envoyée par le téléphone du livreur pendant une livraison en cours.
    /// L'historique des positions forme la trace du trajet parcouru.
    /// </summary>
    public class PositionLivreur
    {
        public int Id { get; set; }

        [Range(-90, 90)]
        public double Latitude { get; set; }

        [Range(-180, 180)]
        public double Longitude { get; set; }

        /// <summary>Rayon d'incertitude annoncé par le GPS du téléphone.</summary>
        [Display(Name = "Précision (m)")]
        public double? PrecisionMetres { get; set; }

        [Display(Name = "Vitesse (km/h)")]
        public double? VitesseKmh { get; set; }

        /// <summary>Direction du déplacement, en degrés (0 = nord).</summary>
        public double? Cap { get; set; }

        public DateTime DateEnregistrement { get; set; } = DateTime.Now;

        // Relations
        public int LivraisonId { get; set; }
        [ForeignKey("LivraisonId")]
        public virtual Livraison? Livraison { get; set; }
    }
}
