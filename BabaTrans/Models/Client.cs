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
        [Phone]
        public string Telephone { get; set; } = string.Empty;

        [Display(Name = "Email")]
        [EmailAddress]
        public string? Email { get; set; }

        [Display(Name = "Personne de contact")]
        [StringLength(200)]
        public string? PersonneContact { get; set; }

        public DateTime DateCreation { get; set; } = DateTime.Now;
        public bool EstActif { get; set; } = true;

        // Navigation
        public virtual ICollection<Commande> Commandes { get; set; } = new List<Commande>();
    }
}
