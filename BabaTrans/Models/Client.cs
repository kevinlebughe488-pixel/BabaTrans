using System.ComponentModel.DataAnnotations;

namespace BabaTrans.Models
{
    /// <summary>
    /// Représente un client (supermarché) de BABA-Trans.
    /// </summary>
    public class Client
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Le nom du supermarché est obligatoire.")]
        [Display(Name = "Nom du Supermarché")]
        [StringLength(200)]
        public string NomSupermarche { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'adresse est obligatoire.")]
        [Display(Name = "Adresse")]
        [StringLength(500)]
        public string Adresse { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le téléphone est obligatoire.")]
        [Display(Name = "Téléphone")]
        [Phone(ErrorMessage = "Le numéro de téléphone n'est pas valide.")]
        [StringLength(30, ErrorMessage = "Le téléphone ne doit pas dépasser 30 caractères.")]
        public string Telephone { get; set; } = string.Empty;

        [Display(Name = "Email")]
        [EmailAddress(ErrorMessage = "L'adresse email n'est pas valide.")]
        [StringLength(200)]
        public string? Email { get; set; }

        [Display(Name = "Personne de contact")]
        [StringLength(200)]
        public string? PersonneContact { get; set; }

        // Emplacement du supermarché : point de départ des livraisons pour le calcul de la distance.
        // Sans emplacement, la distance est calculée depuis le dépôt BABA-Trans.
        [Display(Name = "Latitude")]
        [Range(-90, 90, ErrorMessage = "La latitude doit être comprise entre -90 et 90.")]
        public double? Latitude { get; set; }

        [Display(Name = "Longitude")]
        [Range(-180, 180, ErrorMessage = "La longitude doit être comprise entre -180 et 180.")]
        public double? Longitude { get; set; }

        public DateTime DateCreation { get; set; } = DateTime.Now;
        public bool EstActif { get; set; } = true;

        // Navigation
        public virtual ICollection<Commande> Commandes { get; set; } = new List<Commande>();
    }
}
