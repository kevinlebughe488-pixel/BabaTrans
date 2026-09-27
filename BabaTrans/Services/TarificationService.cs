using BabaTrans.Models;

namespace BabaTrans.Services
{
    public sealed record EstimationTarifaire(
        decimal PoidsTotalKg,
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

        public TarificationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ParametresTarification LireParametres()
        {
            var supplements = _configuration.GetSection("Tarification:Destinations")
                .GetChildren()
                .Where(item => decimal.TryParse(item.Value, out _))
                .ToDictionary(item => item.Key, item => decimal.Parse(item.Value!));

            return new ParametresTarification(
                LireDecimal("Tarification:ForfaitBase", 2000m),
                LireDecimal("Tarification:TarifParKg", 150m),
                LireDecimal("Tarification:SupplementDestinationParDefaut", 3000m),
                LireDecimal("Tarification:MinimumFacturable", 5000m),
                supplements);
        }

        public EstimationTarifaire Calculer(Commande commande, decimal? poidsEstimeKg = null)
        {
            var parametres = LireParametres();
            var poidsTotal = poidsEstimeKg ?? (decimal)(commande.Colis?.Sum(c => c.Poids) ?? 0);
            var forfaitBase = parametres.ForfaitBase;
            var tarifParKg = parametres.TarifParKg;
            var minimum = parametres.MinimumFacturable;
            var supplementDestination = LireSupplementDestination(commande.VilleDestination);
            var tarifPoids = poidsTotal * tarifParKg;
            var total = Math.Max(minimum, forfaitBase + tarifPoids + supplementDestination);

            return new EstimationTarifaire(
                Math.Round(poidsTotal, 2),
                forfaitBase,
                supplementDestination,
                tarifPoids,
                ArrondirAuMillier(total));
        }

        private decimal LireSupplementDestination(string destination)
        {
            var destinations = _configuration.GetSection("Tarification:Destinations");
            var destinationNormalisee = Normaliser(destination);
            var configuration = destinations.GetChildren()
                .FirstOrDefault(item => Normaliser(item.Key) == destinationNormalisee);

            return decimal.TryParse(configuration?.Value, out var supplement)
                ? supplement
                : LireDecimal("Tarification:SupplementDestinationParDefaut", 3000m);
        }

        private decimal LireDecimal(string cle, decimal valeurParDefaut)
        {
            return decimal.TryParse(_configuration[cle], out var valeur)
                ? valeur
                : valeurParDefaut;
        }

        private static decimal ArrondirAuMillier(decimal montant)
        {
            return Math.Ceiling(montant / 1000m) * 1000m;
        }

        private static string Normaliser(string? valeur)
        {
            return (valeur ?? string.Empty).Trim().ToUpperInvariant();
        }
    }
}
