using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using BabaTrans.Models;

namespace BabaTrans.Helpers
{
    /// <summary>
    /// Ajoute le nom complet de l'utilisateur dans son cookie de connexion.
    /// La barre latérale et le tableau de bord n'ont ainsi plus besoin d'interroger la base à chaque page.
    /// </summary>
    public class BabaTransClaimsPrincipalFactory : UserClaimsPrincipalFactory<Utilisateur, IdentityRole>
    {
        public const string ClaimNomComplet = "babatrans:nom_complet";

        public BabaTransClaimsPrincipalFactory(
            UserManager<Utilisateur> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<IdentityOptions> options)
            : base(userManager, roleManager, options) { }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Utilisateur user)
        {
            var identity = await base.GenerateClaimsAsync(user);
            identity.AddClaim(new Claim(ClaimNomComplet, $"{user.Prenom} {user.Nom}".Trim()));
            return identity;
        }
    }

    public static class UtilisateurClaimsExtensions
    {
        public static string NomComplet(this ClaimsPrincipal user)
        {
            var nom = user.FindFirst(BabaTransClaimsPrincipalFactory.ClaimNomComplet)?.Value;
            return string.IsNullOrWhiteSpace(nom) ? user.Identity?.Name ?? "Utilisateur" : nom;
        }

        public static string RolePrincipal(this ClaimsPrincipal user)
            => user.FindFirst(ClaimTypes.Role)?.Value ?? "Utilisateur";

        public static string Initiales(this ClaimsPrincipal user)
        {
            var mots = user.NomComplet().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initiales = string.Concat(mots.Take(2).Select(m => char.ToUpperInvariant(m[0])));
            return string.IsNullOrEmpty(initiales) ? "BT" : initiales;
        }

        public static bool EstPersonnel(this ClaimsPrincipal user)
            => user.IsInRole("Administrateur") || user.IsInRole("Agent");
    }
}
