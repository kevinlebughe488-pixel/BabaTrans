using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BabaTrans.Models
{
    /// <summary>
    /// Représente une commande passée par un client (supermarché).
    /// </summary>
    public class Commande
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La date de commande est obligatoire.")]
        [Display(Name = "Date de Commande")]
        [DataType(DataType.Date)]
        public DateTime DateCommande { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "La ville de destination est obligatoire.")]
        [Display(Name = "Ville de Destination")]
        [StringLength(200)]
        public string VilleDestination { get; set; } = string.Empty;

        [Display(Name = "Description")]
        [StringLength(500)]
        public string? Description { get; set; }

        // Ces champs n'étaient pas stockés en base et étaient perdus à l'enregistrement.
        // Ils sont désormais persistés (voir DbInitializer.MettreAJourSchemaAsync).
        [Display(Name = "Poids estimé (kg)")]
        [Range(0.1, 10000, ErrorMessage = "Le poids estimé doit être compris entre 0,1 et 10 000 kg.")]
        public decimal? PoidsEstimeKg { get; set; }

        [Display(Name = "Adresse exacte de livraison")]
        [StringLength(250)]
        public string? AdresseDestination { get; set; }

        [Display(Name = "Quartier / commune")]
        [StringLength(150)]
        public string? QuartierDestination { get; set; }

        [Display(Name = "Contact destinataire")]
        [StringLength(150)]
        public string? ContactDestination { get; set; }

        [Display(Name = "Téléphone destinataire")]
        [StringLength(50)]
        public string? TelephoneDestination { get; set; }

        // Géolocalisation : seul le point de destination vient du formulaire (carte).
        // Le départ, la distance et la durée sont calculés par le serveur à l'enregistrement.
        [Display(Name = "Latitude de destination")]
        [Range(-90, 90, ErrorMessage = "La latitude doit être comprise entre -90 et 90.")]
        public double? LatitudeDestination { get; set; }

        [Display(Name = "Longitude de destination")]
        [Range(-180, 180, ErrorMessage = "La longitude doit être comprise entre -180 et 180.")]
        public double? LongitudeDestination { get; set; }

        /// <summary>Point d'enlèvement retenu au calcul : le supermarché s'il est géolocalisé, sinon le dépôt.</summary>
        public double? LatitudeDepart { get; set; }
        public double? LongitudeDepart { get; set; }

        [Display(Name = "Distance (km)")]
        public decimal? DistanceKm { get; set; }

        [Display(Name = "Durée estimée (min)")]
        public int? DureeEstimeeMinutes { get; set; }

        [Display(Name = "Statut")]
        public StatutCommande Statut { get; set; } = StatutCommande.EnAttente;

        // Relations
        [Required(ErrorMessage = "Sélectionnez un supermarché.")]
        [Display(Name = "Supermarché")]
        public int ClientId { get; set; }
        [ForeignKey("ClientId")]
        public virtual Client? Client { get; set; }

        // Navigation
        public virtual ICollection<Colis> Colis { get; set; } = new List<Colis>();
    }

    public enum StatutCommande
    {
        [Display(Name = "En attente")]
        EnAttente,
        [Display(Name = "En cours")]
        EnCours,
        [Display(Name = "Livrée")]
        Livree,
        [Display(Name = "Annulée")]
        Annulee
    }
}
