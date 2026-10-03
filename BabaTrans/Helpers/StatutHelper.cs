using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using BabaTrans.Models;

namespace BabaTrans.Helpers
{
    /// <summary>
    /// Libellés lisibles et couleurs de badge des statuts.
    /// Centralisé ici pour que chaque page affiche un statut de la même façon.
    /// </summary>
    public static class StatutHelper
    {
        /// <summary>Retourne le libellé [Display(Name)] d'une valeur d'enum (ex : "QR-Code Généré").</summary>
        public static string Libelle(this Enum valeur)
        {
            var membre = valeur.GetType().GetMember(valeur.ToString()).FirstOrDefault();
            return membre?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? valeur.ToString();
        }

        public static string Badge(this StatutColis statut) => statut switch
        {
            StatutColis.Enregistre => "bt-badge-warning",
            StatutColis.QRCodeGenere => "bt-badge-primary",
            StatutColis.EnTransit => "bt-badge-orange",
            StatutColis.Arrive => "bt-badge-info",
            StatutColis.Livre => "bt-badge-accent",
            StatutColis.Retourne => "bt-badge-danger",
            _ => "bt-badge-neutral"
        };

        public static string Badge(this StatutCommande statut) => statut switch
        {
            StatutCommande.EnAttente => "bt-badge-warning",
            StatutCommande.EnCours => "bt-badge-orange",
            StatutCommande.Livree => "bt-badge-accent",
            StatutCommande.Annulee => "bt-badge-danger",
            _ => "bt-badge-neutral"
        };

        public static string Badge(this StatutLivraison statut) => statut switch
        {
            StatutLivraison.EnAttente => "bt-badge-warning",
            StatutLivraison.EnCours => "bt-badge-orange",
            StatutLivraison.Livree => "bt-badge-accent",
            StatutLivraison.Echouee => "bt-badge-danger",
            _ => "bt-badge-neutral"
        };

        /// <summary>Une commande est "ouverte" tant qu'elle peut encore recevoir des colis.</summary>
        public static bool EstOuverte(this StatutCommande statut)
            => statut == StatutCommande.EnAttente || statut == StatutCommande.EnCours;

        /// <summary>Un colis peut être affecté à une livraison tant qu'il n'est pas parti.</summary>
        public static bool EstAffectable(this StatutColis statut)
            => statut == StatutColis.Enregistre || statut == StatutColis.QRCodeGenere;

        /// <summary>Compare deux noms de ville sans tenir compte de la casse, des accents ni des espaces.</summary>
        public static bool MemeVille(string? a, string? b)
            => CultureInfo.InvariantCulture.CompareInfo.Compare(
                (a ?? string.Empty).Trim(),
                (b ?? string.Empty).Trim(),
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;
    }
}
