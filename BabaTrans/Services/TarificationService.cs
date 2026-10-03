using BabaTrans.Helpers;
using BabaTrans.Models;

namespace BabaTrans.Services
{
    /// <param name="PoidsReel">Vrai si le poids vient des colis pesés, faux s'il s'agit du poids estimé de la commande.</param>
    public sealed record EstimationTarifaire(
        decimal PoidsTotalKg,
        bool PoidsReel,
        decimal ForfaitBase,
        decimal SupplementDestination,
        decimal TarifPoids,
        decimal Total);

    public sealed record ParametresTarification(
        decimal ForfaitBase,
        decimal TarifParKg,
        decimal SupplementDestinationParDefaut,
        decimal MinimumFacturable,
        IReadOnlyDictionary<string, decimal> SupplementsDestination);

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
            if (_parametres != null)
                return _parametres;

            var supplements = _configuration.GetSection("Tarification:Destinations")
                .GetChildren()
                .Where(item => decimal.TryParse(item.Value, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                .ToDictionary(
                    item => item.Key,
                    item => decimal.Parse(item.Value!, System.Globalization.CultureInfo.InvariantCulture));

            _parametres = new ParametresTarification(
                LireDecimal("Tarification:ForfaitBase", 2000m),
                LireDecimal("Tarification:TarifParKg", 150m),
                LireDecimal("Tarification:SupplementDestinationParDefaut", 3000m),
                LireDecimal("Tarification:MinimumFacturable", 5000m),
                supplements);
            return _parametres;
        }

        /// <summary>
        /// Calcule le tarif d'une commande. Le poids réel des colis enregistrés est prioritaire ;
        /// tant qu'aucun colis n'est pesé, on utilise le poids estimé saisi à la commande.
        /// </summary>
        public EstimationTarifaire Calculer(Commande commande)
        {
            var parametres = LireParametres();
            var poidsColis = (decimal)(commande.Colis?.Sum(c => c.Poids) ?? 0);
            var poidsReel = poidsColis > 0;
            var poidsTotal = poidsReel ? poidsColis : commande.PoidsEstimeKg ?? 0m;

            var supplementDestination = LireSupplementDestination(commande.VilleDestination);
            var tarifPoids = poidsTotal * parametres.TarifParKg;
            var total = Math.Max(parametres.MinimumFacturable, parametres.ForfaitBase + tarifPoids + supplementDestination);

            return new EstimationTarifaire(
                Math.Round(poidsTotal, 2),
                poidsReel,
                parametres.ForfaitBase,
                supplementDestination,
                tarifPoids,
                ArrondirAuMillier(total));
        }

        private decimal LireSupplementDestination(string? destination)
        {
            var parametres = LireParametres();
            foreach (var (ville, supplement) in parametres.SupplementsDestination)
            {
                if (StatutHelper.MemeVille(ville, destination))
                    return supplement;
            }
            return parametres.SupplementDestinationParDefaut;
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
