using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using BabaTrans.Helpers;
using BabaTrans.Models;

namespace BabaTrans.Services
{
    public readonly record struct PointGps(double Latitude, double Longitude);

    /// <param name="Routier">Vrai si la distance suit le réseau routier, faux pour une estimation à vol d'oiseau corrigée.</param>
    /// <param name="Trace">Points [latitude, longitude] du tracé à dessiner sur la carte.</param>
    public sealed record Itineraire(decimal DistanceKm, int DureeMinutes, bool Routier, IReadOnlyList<double[]> Trace);

    public sealed record PointDepart(PointGps Point, string Libelle);

    /// <summary>
    /// Calculs géographiques : point de départ d'une commande, distance par la route, durée et zone desservie.
    /// L'itinéraire routier est demandé à un service OSRM ; s'il ne répond pas, la distance à vol d'oiseau
    /// est multipliée par un coefficient routier, ce qui garantit toujours un tarif.
    /// </summary>
    public class GeolocalisationService
    {
        // Emprise de la RDC : BABA-Trans ne livre pas en dehors du pays.
        private const double LatitudeMin = -13.5, LatitudeMax = 5.5, LongitudeMin = 12.0, LongitudeMax = 31.5;
        private const double RayonTerreKm = 6371.0;

        // Un itinéraire routier plus de 2,5 fois plus long que la ligne droite passe sans doute par un pays voisin
        // (réseau routier incomplet) : il n'est pas retenu pour la facturation.
        private const double RapportMaxRouteSurVolOiseau = 2.5;

        private readonly HttpClient _http;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeolocalisationService> _logger;

        public GeolocalisationService(HttpClient http, IMemoryCache cache, IConfiguration configuration, ILogger<GeolocalisationService> logger)
        {
            _http = http;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;

            Depot = new PointDepart(
                new PointGps(LireDouble("Geolocalisation:Depot:Latitude", -4.3219), LireDouble("Geolocalisation:Depot:Longitude", 15.3222)),
                configuration["Geolocalisation:Depot:Nom"] ?? "Dépôt BABA-Trans");
            CoefficientRoutier = LireDouble("Geolocalisation:CoefficientRoutier", 1.3);
            VitesseMoyenneKmh = LireDouble("Geolocalisation:VitesseMoyenneKmh", 40);
            Villes = LireVilles();
        }

        public PointDepart Depot { get; }
        public double CoefficientRoutier { get; }
        public double VitesseMoyenneKmh { get; }

        /// <summary>Villes desservies et coordonnées de leur centre (suggestions et repli sans carte).</summary>
        public IReadOnlyDictionary<string, PointGps> Villes { get; }

        public static bool EstDansZoneCouverte(double latitude, double longitude)
            => latitude is >= LatitudeMin and <= LatitudeMax && longitude is >= LongitudeMin and <= LongitudeMax;

        /// <summary>Les livraisons partent du supermarché s'il est géolocalisé, sinon du dépôt BABA-Trans.</summary>
        public PointDepart PointDeDepart(Client? client)
            => client?.Latitude is double latitude && client.Longitude is double longitude
                ? new PointDepart(new PointGps(latitude, longitude), client.NomSupermarche)
                : Depot;

        public PointGps? CoordonneesVille(string? ville)
        {
            foreach (var (nom, point) in Villes)
            {
                if (StatutHelper.MemeVille(nom, ville))
                    return point;
            }
            return null;
        }

        /// <summary>Distance orthodromique (formule de haversine).</summary>
        public static double DistanceVolOiseauKm(PointGps a, PointGps b)
        {
            static double Radians(double degres) => degres * Math.PI / 180;

            var dLat = Radians(b.Latitude - a.Latitude);
            var dLon = Radians(b.Longitude - a.Longitude);
            var h = Math.Pow(Math.Sin(dLat / 2), 2)
                    + Math.Cos(Radians(a.Latitude)) * Math.Cos(Radians(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
            return 2 * RayonTerreKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        /// <summary>Distance par la route estimée sans service externe : vol d'oiseau × coefficient routier.</summary>
        public double DistanceRouteEstimeeKm(PointGps a, PointGps b)
            => DistanceVolOiseauKm(a, b) * CoefficientRoutier;

        public int DureeEstimeeMinutes(double distanceKm)
            => (int)Math.Ceiling(distanceKm / VitesseMoyenneKmh * 60);

        /// <summary>Itinéraire estimé hors ligne (aucun appel réseau) : utilisé au démarrage et en secours.</summary>
        public Itineraire ItineraireEstime(PointGps depart, PointGps arrivee)
        {
            var distance = DistanceRouteEstimeeKm(depart, arrivee);
            return new Itineraire(
                Math.Round((decimal)distance, 1),
                DureeEstimeeMinutes(distance),
                Routier: false,
                new[] { new[] { depart.Latitude, depart.Longitude }, new[] { arrivee.Latitude, arrivee.Longitude } });
        }

        public async Task<Itineraire> CalculerItineraireAsync(PointGps depart, PointGps arrivee, CancellationToken annulation = default)
        {
            var cle = string.Create(CultureInfo.InvariantCulture,
                $"itineraire:{depart.Latitude:F5},{depart.Longitude:F5};{arrivee.Latitude:F5},{arrivee.Longitude:F5}");
            if (_cache.TryGetValue(cle, out Itineraire? enCache) && enCache != null)
                return enCache;

            var routier = await CalculerItineraireRoutierAsync(depart, arrivee, annulation);

            // Un échec n'est mémorisé que peu de temps, pour réessayer le service routier ensuite.
            var itineraire = routier ?? ItineraireEstime(depart, arrivee);
            _cache.Set(cle, itineraire, routier != null ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(2));
            return itineraire;
        }

        private async Task<Itineraire?> CalculerItineraireRoutierAsync(PointGps depart, PointGps arrivee, CancellationToken annulation)
        {
            var service = _configuration["Geolocalisation:ServiceItineraire"];
            if (string.IsNullOrWhiteSpace(service))
                return null;

            // Les coordonnées sont écrites avec un point décimal quelle que soit la culture du serveur.
            var url = string.Create(CultureInfo.InvariantCulture,
                $"{service.TrimEnd('/')}/route/v1/driving/{depart.Longitude},{depart.Latitude};{arrivee.Longitude},{arrivee.Latitude}?overview=simplified&geometries=geojson");

            try
            {
                using var reponse = await _http.GetAsync(url, annulation);
                if (!reponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Service d'itinéraire indisponible (HTTP {Statut}) : distance estimée à vol d'oiseau.", (int)reponse.StatusCode);
                    return null;
                }

                await using var flux = await reponse.Content.ReadAsStreamAsync(annulation);
                using var json = await JsonDocument.ParseAsync(flux, cancellationToken: annulation);
                var racine = json.RootElement;
                if (!racine.TryGetProperty("code", out var code) || code.GetString() != "Ok"
                    || !racine.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                    return null;

                var route = routes[0];
                var distanceKm = route.GetProperty("distance").GetDouble() / 1000;
                var dureeMinutes = route.GetProperty("duration").GetDouble() / 60;

                var volOiseau = DistanceVolOiseauKm(depart, arrivee);
                if (volOiseau > 20 && distanceKm > volOiseau * RapportMaxRouteSurVolOiseau)
                {
                    _logger.LogInformation("Itinéraire routier de {Route:F0} km écarté (ligne droite : {VolOiseau:F0} km).", distanceKm, volOiseau);
                    return null;
                }

                // GeoJSON donne [longitude, latitude] ; Leaflet attend [latitude, longitude].
                var trace = route.GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
                    .Select(point => new[] { point[1].GetDouble(), point[0].GetDouble() })
                    .ToList();

                return new Itineraire(Math.Round((decimal)distanceKm, 1), (int)Math.Ceiling(dureeMinutes), Routier: true, trace);
            }
            catch (OperationCanceledException) when (annulation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
                                           or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                _logger.LogWarning(ex, "Calcul d'itinéraire routier impossible : distance estimée à vol d'oiseau.");
                return null;
            }
        }

        private Dictionary<string, PointGps> LireVilles()
        {
            var villes = new Dictionary<string, PointGps>();
            foreach (var ville in _configuration.GetSection("Geolocalisation:Villes").GetChildren())
            {
                if (TryLireDouble(ville["Latitude"], out var latitude) && TryLireDouble(ville["Longitude"], out var longitude))
                    villes[ville.Key] = new PointGps(latitude, longitude);
            }
            return villes;
        }

        private double LireDouble(string cle, double valeurParDefaut)
            => TryLireDouble(_configuration[cle], out var valeur) ? valeur : valeurParDefaut;

        private static bool TryLireDouble(string? texte, out double valeur)
            => double.TryParse(texte, NumberStyles.Float, CultureInfo.InvariantCulture, out valeur);
    }
}
