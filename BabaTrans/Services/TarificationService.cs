using BabaTrans.Models;

namespace BabaTrans.Services
{
    /// <param name="PoidsReel">Vrai si le poids vient des colis pesés, faux s'il s'agit du poids estimé de la commande.</param>
    /// <param name="DistanceKm">Distance de livraison ; absente pour une commande qui n'a pas encore été géolocalisée.</param>
    public sealed record EstimationTarifaire(
        decimal PoidsTotalKg,
        bool PoidsReel,
        decimal? DistanceKm,
        decimal ForfaitBase,
        decimal TarifPoids,
        decimal TarifDistance,
        decimal Total);

    public sealed record ParametresTarification(
        decimal ForfaitBase,
        decimal TarifParKg,
        decimal TarifParKm,
        decimal MinimumFacturable);

    /// <summary>
    /// Tarif d'une livraison = forfait + poids × tarif au kg + distance × tarif au km,
    /// avec un minimum facturable, arrondi au millier de francs supérieur.
    /// </summary>
    public class TarificationService
    {
        private readonly IConfiguration _configuration;
        private ParametresTarification? _parametres;

        public TarificationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>Lit les paramètres une seule fois par requête (le service est enregistré en Scoped).</summary>
        public ParametresTarification LireParametres()
        {
            return _parametres ??= new ParametresTarification(
                LireDecimal("Tarification:ForfaitBase", 2000m),
                LireDecimal("Tarification:TarifParKg", 150m),
                LireDecimal("Tarification:TarifParKm", 100m),
                LireDecimal("Tarification:MinimumFacturable", 5000m));
        }

        /// <summary>
        /// Calcule le tarif d'une commande. Le poids réel des colis enregistrés est prioritaire ;
        /// tant qu'aucun colis n'est pesé, on utilise le poids estimé saisi à la commande.
        /// </summary>
        public EstimationTarifaire Calculer(Commande commande)
        {
            var poidsColis = (decimal)(commande.Colis?.Sum(c => c.Poids) ?? 0);
            var poidsReel = poidsColis > 0;
            var poidsTotal = poidsReel ? poidsColis : commande.PoidsEstimeKg ?? 0m;
            return Calculer(poidsTotal, poidsReel, commande.DistanceKm);
        }

        public EstimationTarifaire Calculer(decimal poidsKg, bool poidsReel, decimal? distanceKm)
        {
            var parametres = LireParametres();
            var tarifPoids = Math.Round(poidsKg * parametres.TarifParKg);
            var tarifDistance = Math.Round((distanceKm ?? 0m) * parametres.TarifParKm);
            var total = Math.Max(parametres.MinimumFacturable, parametres.ForfaitBase + tarifPoids + tarifDistance);

            return new EstimationTarifaire(
                Math.Round(poidsKg, 2),
                poidsReel,
                distanceKm,
                parametres.ForfaitBase,
                tarifPoids,
                tarifDistance,
                ArrondirAuMillier(total));
        }

        private decimal LireDecimal(string cle, decimal valeurParDefaut)
        {
            return decimal.TryParse(_configuration[cle], System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var valeur)
                ? valeur
                : valeurParDefaut;
        }

        private static decimal ArrondirAuMillier(decimal montant)
        {
            return Math.Ceiling(montant / 1000m) * 1000m;
        }
    }
}
