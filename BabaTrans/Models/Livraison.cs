using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BabaTrans.Models
{
    /// <summary>
    /// Représente une livraison effectuée par un livreur.
    /// </summary>
    public class Livraison
    {
        public int Id { get; set; }

        [Display(Name = "Date de Livraison")]
        [DataType(DataType.DateTime)]
        public DateTime? DateLivraison { get; set; }

        [Display(Name = "Statut")]
        public StatutLivraison Statut { get; set; } = StatutLivraison.EnAttente;

        [Display(Name = "Date de départ")]
        public DateTime? DateDepart { get; set; }

        [Display(Name = "Date d'arrivée")]
        public DateTime? DateArrivee { get; set; }

        [Display(Name = "Commentaire")]
        [StringLength(500)]
        public string? Commentaire { get; set; }

        // Relations
        [Required]
        [Display(Name = "Colis")]
        public int ColisId { get; set; }
        [ForeignKey("ColisId")]
        public virtual Colis? Colis { get; set; }

        [Display(Name = "Livreur")]
        public string? LivreurId { get; set; }
        [ForeignKey("LivreurId")]
        public virtual Utilisateur? Livreur { get; set; }

        [Display(Name = "Moyen de Transport")]
        public int? MoyenTransportId { get; set; }
        [ForeignKey("MoyenTransportId")]
        public virtual MoyenTransport? MoyenTransport { get; set; }
    }

    public enum StatutLivraison
    {
        [Display(Name = "En Attente")]
        EnAttente,
        [Display(Name = "En Cours")]
        EnCours,
        [Display(Name = "Livrée")]
        Livree,
        [Display(Name = "Échouée")]
        Echouee
    }
}
