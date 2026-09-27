using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BabaTrans.Models
{
    /// <summary>
    /// Représente un colis à livrer.
    /// </summary>
    public class Colis
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La description est obligatoire.")]
        [Display(Name = "Description")]
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le poids est obligatoire.")]
        [Display(Name = "Poids (kg)")]
        [Range(0.1, 10000, ErrorMessage = "Le poids doit être entre 0.1 et 10000 kg.")]
        public double Poids { get; set; }

        [Display(Name = "Statut")]
        public StatutColis Statut { get; set; } = StatutColis.Enregistre;

        [Display(Name = "Date d'enregistrement")]
        public DateTime DateEnregistrement { get; set; } = DateTime.Now;

        [Display(Name = "Code de suivi")]
        [StringLength(50)]
        public string? CodeSuivi { get; set; }

        // Relations
        [Required]
        [Display(Name = "Commande")]
        public int CommandeId { get; set; }
        [ForeignKey("CommandeId")]
        public virtual Commande? Commande { get; set; }

        // Navigation
        public virtual TimbreQRCode? TimbreQRCode { get; set; }
        public virtual ICollection<Livraison> Livraisons { get; set; } = new List<Livraison>();
    }

    public enum StatutColis
    {
        [Display(Name = "Enregistré")]
        Enregistre,
        [Display(Name = "QR-Code Généré")]
        QRCodeGenere,
        [Display(Name = "En Transit")]
        EnTransit,
        [Display(Name = "Arrivé")]
        Arrive,
        [Display(Name = "Livré")]
        Livre,
        [Display(Name = "Retourné")]
        Retourne
    }
}
