using System.ComponentModel.DataAnnotations;

namespace BabaTrans.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "L'email est obligatoire.")]
        [EmailAddress(ErrorMessage = "Veuillez entrer un email valide.")]
        [Display(Name = "Adresse Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mot de passe")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Se souvenir de moi")]
        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [Display(Name = "Nom")]
        [StringLength(100)]
        public string Nom { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom est obligatoire.")]
        [Display(Name = "Prénom")]
        [StringLength(100)]
        public string Prenom { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'email est obligatoire.")]
        [EmailAddress(ErrorMessage = "Veuillez entrer un email valide.")]
        [Display(Name = "Adresse Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mot de passe")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Veuillez confirmer le mot de passe.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmer le mot de passe")]
        [Compare("Password", ErrorMessage = "Les mots de passe ne correspondent pas.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le rôle est obligatoire.")]
        [Display(Name = "Rôle")]
        public string Role { get; set; } = string.Empty;

        // Champs optionnels selon le rôle
        [Display(Name = "Matricule (Agent)")]
        public string? Matricule { get; set; }

        [Display(Name = "Immatriculation (Livreur)")]
        public string? Immatriculation { get; set; }
    }

    public class DashboardViewModel
    {
        public int TotalClients { get; set; }
        public int TotalCommandes { get; set; }
        public int TotalColis { get; set; }
        public int TotalLivraisons { get; set; }
        public int ColisEnTransit { get; set; }
        public int LivraisonsConfirmees { get; set; }
        public int CommandesEnAttente { get; set; }
        public int ColisLivres { get; set; }
        public List<dynamic>? DerniersCommandes { get; set; }
        public List<dynamic>? DerniereActivites { get; set; }
    }

    public class SuiviColisViewModel
    {
        public string? CodeSuivi { get; set; }
        public Models.Colis? Colis { get; set; }
        public bool Recherche { get; set; }
    }
}
