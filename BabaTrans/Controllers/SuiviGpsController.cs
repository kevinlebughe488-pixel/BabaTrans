using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Helpers;
using BabaTrans.Models;
using BabaTrans.Services;

namespace BabaTrans.Controllers
{
    /// <summary>
    /// Suivi GPS des livreurs en temps réel : le téléphone du livreur envoie sa position pendant la livraison,
    /// le personnel et le supermarché expéditeur la consultent sur une carte rafraîchie toutes les quelques secondes.
    /// </summary>
    [Authorize]
    public class SuiviGpsController : Controller
    {
        // Au-delà, la position est considérée comme perdue (téléphone éteint, sans réseau, partage arrêté).
        public const int SecondesAvantSignalPerdu = 120;

        private const int NombreMaxPointsTrace = 500;
        private static readonly TimeSpan IntervalleMinimalEntrePositions = TimeSpan.FromSeconds(3);

        private readonly BabaTransContext _context;
        private readonly UserManager<Utilisateur> _userManager;
        private readonly CompteClientService _compteClient;
        private readonly GeolocalisationService _geolocalisation;

        public SuiviGpsController(BabaTransContext context, UserManager<Utilisateur> userManager,
            CompteClientService compteClient, GeolocalisationService geolocalisation)
        {
            _context = context;
            _userManager = userManager;
            _compteClient = compteClient;
            _geolocalisation = geolocalisation;
        }

        // GET: SuiviGps/Carte : tous les livreurs en route sur une même carte.
        [Authorize(Roles = "Administrateur,Agent")]
        public IActionResult Carte()
        {
            return View();
        }

        // GET: SuiviGps/Flotte : données de la carte de la flotte (JSON).
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Flotte()
        {
            var livraisons = await _context.Livraisons
                .AsNoTracking()
                .Where(l => l.Statut == StatutLivraison.EnCours)
                .OrderBy(l => l.Id)
                .Select(l => new
                {
                    l.Id,
                    l.Colis!.CodeSuivi,
                    Supermarche = l.Colis.Commande!.Client!.NomSupermarche,
                    l.Colis.Commande.VilleDestination,
                    l.Colis.Commande.LatitudeDestination,
                    l.Colis.Commande.LongitudeDestination,
                    Livreur = l.Livreur != null ? l.Livreur.Prenom + " " + l.Livreur.Nom : null,
                    Vehicule = l.MoyenTransport != null ? l.MoyenTransport.Type : null,
                    Derniere = l.Positions
                        .OrderByDescending(p => p.DateEnregistrement)
                        .Select(p => new { p.Latitude, p.Longitude, p.VitesseKmh, p.DateEnregistrement })
                        .FirstOrDefault()
                })
                .ToListAsync();

            var maintenant = DateTime.Now;
            return Json(livraisons.Select(l =>
            {
                var ageSecondes = l.Derniere == null ? (double?)null : Math.Max(0, (maintenant - l.Derniere.DateEnregistrement).TotalSeconds);
                return new
                {
                    livraisonId = l.Id,
                    codeSuivi = l.CodeSuivi,
                    supermarche = l.Supermarche,
                    villeDestination = l.VilleDestination,
                    destination = l.LatitudeDestination is double lat && l.LongitudeDestination is double lng
                        ? new { latitude = lat, longitude = lng }
                        : null,
                    livreur = l.Livreur ?? "Livreur non affecté",
                    vehicule = l.Vehicule,
                    position = l.Derniere == null ? null : new
                    {
                        latitude = l.Derniere.Latitude,
                        longitude = l.Derniere.Longitude,
                        vitesse = l.Derniere.VitesseKmh,
                        ageSecondes
                    },
                    gpsActif = ageSecondes <= SecondesAvantSignalPerdu,
                    url = Url.Action("Details", "Livraison", new { id = l.Id })
                };
            }));
        }

        // GET: SuiviGps/Position/5 : dernière position du livreur, trace parcourue et arrivée estimée (JSON).
        public async Task<IActionResult> Position(int id)
        {
            var livraison = await _context.Livraisons
                .AsNoTracking()
                .Include(l => l.Colis).ThenInclude(c => c!.Commande)
                .Include(l => l.Livreur)
                .Include(l => l.MoyenTransport)
                .FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!await PeutSuivreAsync(livraison))
                return Forbid();

            // On lit les derniers points puis on les remet dans l'ordre chronologique pour tracer le parcours.
            var positions = await _context.PositionsLivreur
                .AsNoTracking()
                .Where(p => p.LivraisonId == id)
                .OrderByDescending(p => p.DateEnregistrement)
                .Take(NombreMaxPointsTrace)
                .ToListAsync();
            positions.Reverse();

            var commande = livraison.Colis?.Commande;
            var destination = PointDestination(commande);
            var depart = commande?.LatitudeDepart is double latDepart && commande.LongitudeDepart is double lngDepart
                ? new PointGps(latDepart, lngDepart)
                : _geolocalisation.Depot.Point;
            var derniere = positions.LastOrDefault();
            var enCours = livraison.Statut == StatutLivraison.EnCours;

            double? ageSecondes = derniere == null ? null : Math.Max(0, (DateTime.Now - derniere.DateEnregistrement).TotalSeconds);
            double? distanceRestanteKm = null;
            int? dureeRestanteMinutes = null;
            if (enCours && derniere != null && destination != null)
            {
                var restante = _geolocalisation.DistanceRouteEstimeeKm(new PointGps(derniere.Latitude, derniere.Longitude), destination.Value);
                distanceRestanteKm = Math.Round(restante, 1);
                dureeRestanteMinutes = _geolocalisation.DureeEstimeeMinutes(restante);
            }

            return Json(new
            {
                livraisonId = livraison.Id,
                statut = livraison.Statut.ToString(),
                statutLibelle = livraison.Statut.Libelle(),
                suiviActif = enCours,
                livreur = livraison.Livreur != null ? $"{livraison.Livreur.Prenom} {livraison.Livreur.Nom}" : null,
                vehicule = livraison.MoyenTransport?.Type,
                depart = new { latitude = depart.Latitude, longitude = depart.Longitude },
                destination = destination == null ? null : new
                {
                    latitude = destination.Value.Latitude,
                    longitude = destination.Value.Longitude,
                    libelle = LibelleDestination(commande!)
                },
                position = derniere == null ? null : new
                {
                    latitude = derniere.Latitude,
                    longitude = derniere.Longitude,
                    precision = derniere.PrecisionMetres,
                    vitesse = derniere.VitesseKmh,
                    cap = derniere.Cap,
                    ageSecondes,
                    heure = derniere.DateEnregistrement.ToString("HH:mm:ss")
                },
                gpsActif = enCours && ageSecondes <= SecondesAvantSignalPerdu,
                trace = positions.Select(p => new[] { p.Latitude, p.Longitude }),
                distanceRestanteKm,
                dureeRestanteMinutes,
                arriveeEstimee = dureeRestanteMinutes.HasValue ? DateTime.Now.AddMinutes(dureeRestanteMinutes.Value).ToString("HH:mm") : null
            });
        }

        // POST: SuiviGps/EnvoyerPosition/5 : appelé par le téléphone du livreur pendant la livraison.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Livreur")]
        public async Task<IActionResult> EnvoyerPosition(int id, double latitude, double longitude, double? precision, double? vitesse, double? cap)
        {
            if (!ModelState.IsValid || latitude is < -90 or > 90 || longitude is < -180 or > 180)
                return BadRequest(new { message = "Position GPS invalide." });

            var livraison = await _context.Livraisons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();

            // Seul le livreur affecté partage sa position, et seulement pendant l'acheminement.
            if (livraison.LivreurId != _userManager.GetUserId(User))
                return Forbid();
            if (livraison.Statut != StatutLivraison.EnCours)
                return Conflict(new { arreter = true, message = "Le suivi GPS s'arrête : cette livraison n'est plus en cours." });

            var derniereDate = await _context.PositionsLivreur
                .Where(p => p.LivraisonId == id)
                .OrderByDescending(p => p.DateEnregistrement)
                .Select(p => (DateTime?)p.DateEnregistrement)
                .FirstOrDefaultAsync();
            var maintenant = DateTime.Now;
            if (derniereDate.HasValue && maintenant - derniereDate.Value < IntervalleMinimalEntrePositions)
                return Json(new { enregistre = false });

            _context.PositionsLivreur.Add(new PositionLivreur
            {
                LivraisonId = id,
                Latitude = latitude,
                Longitude = longitude,
                PrecisionMetres = precision is >= 0 and < 100000 ? Math.Round(precision.Value, 1) : null,
                // Le navigateur donne la vitesse en m/s.
                VitesseKmh = vitesse is >= 0 and < 100 ? Math.Round(vitesse.Value * 3.6, 1) : null,
                Cap = cap is >= 0 and < 360 ? Math.Round(cap.Value) : null,
                DateEnregistrement = maintenant
            });
            await _context.SaveChangesAsync();

            return Json(new { enregistre = true, heure = maintenant.ToString("HH:mm:ss") });
        }

        // GET: SuiviGps/ItineraireRoutier?departLatitude=..&departLongitude=..&arriveeLatitude=..&arriveeLongitude=..
        // Tracé par la route entre deux points, pour les cartes de consultation (fiche commande, fiche supermarché).
        public async Task<IActionResult> ItineraireRoutier(double departLatitude, double departLongitude, double arriveeLatitude, double arriveeLongitude)
        {
            if (!ModelState.IsValid
                || !GeolocalisationService.EstDansZoneCouverte(departLatitude, departLongitude)
                || !GeolocalisationService.EstDansZoneCouverte(arriveeLatitude, arriveeLongitude))
                return BadRequest(new { message = "Les deux points doivent se trouver en RDC." });

            var itineraire = await _geolocalisation.CalculerItineraireAsync(
                new PointGps(departLatitude, departLongitude), new PointGps(arriveeLatitude, arriveeLongitude), HttpContext.RequestAborted);
            return Json(new
            {
                distanceKm = itineraire.DistanceKm,
                dureeMinutes = itineraire.DureeMinutes,
                routier = itineraire.Routier,
                trace = itineraire.Trace
            });
        }

        /// <summary>Personnel, livreur affecté, ou supermarché à qui appartient le colis.</summary>
        private async Task<bool> PeutSuivreAsync(Livraison livraison)
        {
            if (User.EstPersonnel())
                return true;

            if (User.IsInRole("Livreur"))
                return livraison.LivreurId == _userManager.GetUserId(User);

            if (User.IsInRole("Client"))
            {
                var client = await _compteClient.GetClientConnecteAsync(User);
                return client != null && livraison.Colis?.Commande?.ClientId == client.Id;
            }

            return false;
        }

        /// <summary>Point de livraison choisi sur la carte ; à défaut, centre de la ville de destination.</summary>
        private PointGps? PointDestination(Commande? commande)
        {
            if (commande == null)
                return null;
            if (commande.LatitudeDestination is double latitude && commande.LongitudeDestination is double longitude)
                return new PointGps(latitude, longitude);
            return _geolocalisation.CoordonneesVille(commande.VilleDestination);
        }

        private static string LibelleDestination(Commande commande)
            => string.Join(", ", new[] { commande.AdresseDestination, commande.QuartierDestination, commande.VilleDestination }
                .Where(partie => !string.IsNullOrWhiteSpace(partie)));
    }
}
