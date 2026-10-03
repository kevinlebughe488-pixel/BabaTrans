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

        // Livreur
        public int LivraisonsEnAttente { get; set; }
        public int LivraisonsEnCours { get; set; }

        // Flux logistique (Administrateur / Agent) : volume réel à chaque étape
        public int ColisAAffecter { get; set; }
        public int LivraisonsAttenteDepart { get; set; }
    }

    public class SuiviColisViewModel
    {
        public string? CodeSuivi { get; set; }
        public Models.Colis? Colis { get; set; }
        public bool Recherche { get; set; }

        /// <summary>
        /// Vrai pour le personnel BABA-Trans et le supermarché propriétaire du colis.
        /// Les autres visiteurs voient seulement l'avancement, sans données nominatives.
        /// </summary>
        public bool AccesComplet { get; set; }

        /// <summary>Livraisons du colis, de la plus récente à la plus ancienne.</summary>
        public List<Models.Livraison> Livraisons { get; set; } = new();
    }

    public class RapportViewModel
    {
        public int TotalClients { get; set; }
        public int ClientsActifs { get; set; }

        public int TotalCommandes { get; set; }
        public int CommandesEnAttente { get; set; }
        public int CommandesEnCours { get; set; }
        public int CommandesLivrees { get; set; }
        public int CommandesAnnulees { get; set; }

        public int TotalColis { get; set; }
        public int ColisAvecTimbre { get; set; }
        public int ColisEnTransit { get; set; }
        public int ColisLivres { get; set; }

        public int TotalLivraisons { get; set; }
        public int LivraisonsConfirmees { get; set; }
        public int LivraisonsEnCours { get; set; }
        public int LivraisonsEchouees { get; set; }

        public int TotalMoyensTransport { get; set; }
        public int MoyensDisponibles { get; set; }
        public int TotalTrajets { get; set; }

        /// <summary>Part des livraisons terminées (réussies ou échouées) qui ont réussi.</summary>
        public int TauxReussite => LivraisonsConfirmees + LivraisonsEchouees == 0
            ? 0
            : (int)Math.Round(100.0 * LivraisonsConfirmees / (LivraisonsConfirmees + LivraisonsEchouees));

        public List<Models.Commande> DernieresCommandes { get; set; } = new();
        public List<Models.Livraison> DernieresLivraisons { get; set; } = new();
    }
}
