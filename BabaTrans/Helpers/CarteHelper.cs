using System.Globalization;

namespace BabaTrans.Helpers
{
    /// <summary>
    /// Écriture des coordonnées GPS dans les vues : toujours avec un point décimal (format attendu par
    /// les cartes et par le serveur), quelle que soit la culture d'affichage.
    /// </summary>
    public static class CarteHelper
    {
        public static string Invariant(double? valeur)
            => valeur?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        /// <summary>« latitude,longitude », ou null si l'une des deux manque.</summary>
        public static string? Coordonnees(double? latitude, double? longitude)
            => latitude is double lat && longitude is double lng
                ? string.Create(CultureInfo.InvariantCulture, $"{lat},{lng}")
                : null;

        /// <summary>Lien d'itinéraire Google Maps (ouvre l'application de navigation sur un téléphone).</summary>
        public static string? LienNavigation(double? latitude, double? longitude)
            => Coordonnees(latitude, longitude) is string destination
                ? "https://www.google.com/maps/dir/?api=1&travelmode=driving&destination=" + Uri.EscapeDataString(destination)
                : null;

        /// <summary>« 6 h 20 » ou « 45 min ».</summary>
        public static string Duree(int? minutes)
        {
            if (minutes is not int total)
                return "—";
            if (total < 60)
                return $"{total} min";
            var heures = total / 60;
            var reste = total % 60;
            return reste == 0 ? $"{heures} h" : $"{heures} h {reste:00}";
        }
    }
}
