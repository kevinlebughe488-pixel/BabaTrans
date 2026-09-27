using Microsoft.AspNetCore.Identity;

namespace BabaTrans.Models
{
    /// <summary>
    /// Classe de base pour tous les utilisateurs du système BABA-Trans.
    /// Hérite de IdentityUser pour l'authentification ASP.NET Identity.
    /// </summary>
    public class Utilisateur : IdentityUser
    {
        public string Nom { get; set; } = string.Empty;
        public string Prenom { get; set; } = string.Empty;
        public DateTime DateCreation { get; set; } = DateTime.Now;
        public bool EstActif { get; set; } = true;

        // Propriétés spécifiques aux rôles (nullable)
        public string? Matricule { get; set; }          // Pour les Agents
        public string? Immatriculation { get; set; }     // Pour les Livreurs
    }
}
