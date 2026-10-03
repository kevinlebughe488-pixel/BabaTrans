using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Helpers;
using BabaTrans.Models;
using BabaTrans.Services;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur,Agent,Client")]
    public class CommandeController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly TarificationService _tarificationService;
        private readonly CompteClientService _compteClient;
        private readonly GeolocalisationService _geolocalisation;

        public CommandeController(BabaTransContext context, TarificationService tarificationService, CompteClientService compteClient,
            GeolocalisationService geolocalisation)
        {
            _context = context;
            _tarificationService = tarificationService;
            _compteClient = compteClient;
            _geolocalisation = geolocalisation;
        }

        public async Task<IActionResult> Index()
        {
            var query = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Colis)
                .AsQueryable();

            if (User.IsInRole("Client"))
            {
                var client = await _compteClient.GetClientConnecteAsync(User);
                if (client == null)
                {
                    TempData["Error"] = "Aucun supermarché n'est associé à ce compte. Contactez BABA-Trans.";
                    return View(new List<Commande>());
                }

                query = query.Where(c => c.ClientId == client.Id);
            }

            var commandes = await query
                .OrderByDescending(c => c.DateCommande)
                .ToListAsync();

            return View(commandes);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var commande = await _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Colis)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (commande == null) return NotFound();

            if (!await PeutConsulterCommandeAsync(commande))
                return Forbid();

            // Seuls les identifiants sont chargés : inutile de ramener les images QR pour afficher un badge.
            var idsColis = commande.Colis.Select(c => c.Id).ToList();
            ViewBag.ColisAvecTimbre = (await _context.TimbresQRCode
                .Where(t => idsColis.Contains(t.ColisId))
                .Select(t => t.ColisId)
                .ToListAsync()).ToHashSet();
            ViewBag.PeutAnnuler = await PeutEtreAnnuleeAsync(commande);

            return View(commande);
        }

        public async Task<IActionResult> Create(int? clientId)
        {
            if (User.IsInRole("Client"))
            {
                var client = await _compteClient.GetClientConnecteAsync(User);
                if (client == null || !client.EstActif)
                {
                    TempData["Error"] = client == null
                        ? "Aucun supermarché n'est associé à ce compte. Contactez BABA-Trans."
                        : "Votre supermarché est désactivé : impossible de déposer une nouvelle commande.";
                    return RedirectToAction(nameof(Index));
                }
            }

            await PreparerFormulaireCommandeAsync(clientId);
            return View(new Commande { ClientId = clientId ?? 0 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("VilleDestination,Description,PoidsEstimeKg,AdresseDestination,QuartierDestination,ContactDestination,TelephoneDestination,ClientId,LatitudeDestination,LongitudeDestination")] Commande commande)
        {
            Client? client;
            if (User.IsInRole("Client"))
            {
                client = await _compteClient.GetClientConnecteAsync(User);
                if (client == null || !client.EstActif)
                {
                    TempData["Error"] = "Aucun supermarché actif n'est associé à ce compte. Contactez BABA-Trans.";
                    return RedirectToAction(nameof(Index));
                }

                // Un client ne choisit pas le supermarché : la commande est toujours la sienne.
                commande.ClientId = client.Id;
                ModelState.Remove(nameof(commande.ClientId));
            }
            else
            {
                client = await ValiderClientAsync(commande.ClientId, clientActuelId: null);
            }

            NettoyerChamps(commande);

            if (!commande.PoidsEstimeKg.HasValue || commande.PoidsEstimeKg <= 0)
                ModelState.AddModelError(nameof(commande.PoidsEstimeKg), "Le poids estimé est obligatoire pour calculer le tarif.");

            if (ModelState.IsValid)
                await DefinirItineraireAsync(commande, client);

            if (ModelState.IsValid)
            {
                commande.DateCommande = DateTime.Now;
                commande.Statut = StatutCommande.EnAttente;
                _context.Add(commande);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Commande CMD-{commande.Id} créée avec succès.";
                return RedirectToAction(nameof(Details), new { id = commande.Id });
            }

            await PreparerFormulaireCommandeAsync(commande.ClientId);
            return View(commande);
        }

        // GET: Commande/EstimerTarif?latitude=..&longitude=..&poids=..&clientId=..&commandeId=..
        // Aperçu pendant la saisie. Le montant définitif est recalculé par le serveur à l'enregistrement.
        public async Task<IActionResult> EstimerTarif(double latitude, double longitude, decimal? poids, int? clientId, int? commandeId)
        {
            if (!ModelState.IsValid || !GeolocalisationService.EstDansZoneCouverte(latitude, longitude))
                return BadRequest(new { message = "Le point de livraison doit se trouver en RDC." });

            // Un client ne peut estimer qu'au départ de son propre supermarché.
            var client = User.IsInRole("Client")
                ? await _compteClient.GetClientConnecteAsync(User)
                : clientId.HasValue ? await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId) : null;

            var depart = _geolocalisation.PointDeDepart(client);
            var itineraire = await _geolocalisation.CalculerItineraireAsync(depart.Point, new PointGps(latitude, longitude), HttpContext.RequestAborted);
            // En modification (personnel uniquement), le poids réel des colis déjà pesés prime, comme sur la fiche commande.
            var poidsColis = commandeId.HasValue && User.EstPersonnel()
                ? (decimal)(await _context.Colis.Where(c => c.CommandeId == commandeId).SumAsync(c => (double?)c.Poids) ?? 0)
                : 0m;
            var estimation = poidsColis > 0
                ? _tarificationService.Calculer(poidsColis, poidsReel: true, itineraire.DistanceKm)
                : poids is > 0 and <= 10000
                    ? _tarificationService.Calculer(poids.Value, poidsReel: false, itineraire.DistanceKm)
                    : null;

            return Json(new
            {
                depart = new { latitude = depart.Point.Latitude, longitude = depart.Point.Longitude, libelle = depart.Libelle },
                distanceKm = itineraire.DistanceKm,
                dureeMinutes = itineraire.DureeMinutes,
                routier = itineraire.Routier,
                trace = itineraire.Trace,
                tarif = estimation == null ? null : new
                {
                    forfait = estimation.ForfaitBase,
                    poids = estimation.TarifPoids,
                    distance = estimation.TarifDistance,
                    ajustement = estimation.Ajustement,
                    libelleAjustement = estimation.LibelleAjustement,
                    total = estimation.Total,
                    poidsReel = estimation.PoidsReel,
                    poidsKg = estimation.PoidsTotalKg
                }
            });
        }

        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var commande = await _context.Commandes.FindAsync(id);
            if (commande == null) return NotFound();

            if (!commande.Statut.EstOuverte())
            {
                TempData["Error"] = $"Une commande {commande.Statut.Libelle().ToLowerInvariant()} ne peut plus être modifiée.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await PreparerFormulaireCommandeAsync(commande.ClientId);
            return View(commande);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,VilleDestination,Description,PoidsEstimeKg,AdresseDestination,QuartierDestination,ContactDestination,TelephoneDestination,ClientId,LatitudeDestination,LongitudeDestination")] Commande commande)
        {
            if (id != commande.Id) return NotFound();

            var commandeExistante = await _context.Commandes.FindAsync(id);
            if (commandeExistante == null) return NotFound();

            if (!commandeExistante.Statut.EstOuverte())
            {
                TempData["Error"] = $"Une commande {commandeExistante.Statut.Libelle().ToLowerInvariant()} ne peut plus être modifiée.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (commande.ClientId != commandeExistante.ClientId && commandeExistante.Statut != StatutCommande.EnAttente)
                ModelState.AddModelError(nameof(commande.ClientId), "Le supermarché ne peut plus être changé une fois l'acheminement commencé.");

            var client = await ValiderClientAsync(commande.ClientId, clientActuelId: commandeExistante.ClientId);
            NettoyerChamps(commande);

            // La distance n'est recalculée que si le point de livraison ou le supermarché (point de départ) change.
            var itineraireAJour = commandeExistante.DistanceKm.HasValue
                && commande.ClientId == commandeExistante.ClientId
                && commande.LatitudeDestination == commandeExistante.LatitudeDestination
                && commande.LongitudeDestination == commandeExistante.LongitudeDestination;
            if (itineraireAJour)
            {
                commande.LatitudeDepart = commandeExistante.LatitudeDepart;
                commande.LongitudeDepart = commandeExistante.LongitudeDepart;
                commande.DistanceKm = commandeExistante.DistanceKm;
                commande.DureeEstimeeMinutes = commandeExistante.DureeEstimeeMinutes;
            }
            else if (ModelState.IsValid)
            {
                await DefinirItineraireAsync(commande, client);
            }

            if (ModelState.IsValid)
            {
                commandeExistante.VilleDestination = commande.VilleDestination;
                commandeExistante.Description = commande.Description;
                commandeExistante.PoidsEstimeKg = commande.PoidsEstimeKg;
                commandeExistante.AdresseDestination = commande.AdresseDestination;
                commandeExistante.QuartierDestination = commande.QuartierDestination;
                commandeExistante.ContactDestination = commande.ContactDestination;
                commandeExistante.TelephoneDestination = commande.TelephoneDestination;
                commandeExistante.ClientId = commande.ClientId;
                commandeExistante.LatitudeDestination = commande.LatitudeDestination;
                commandeExistante.LongitudeDestination = commande.LongitudeDestination;
                commandeExistante.LatitudeDepart = commande.LatitudeDepart;
                commandeExistante.LongitudeDepart = commande.LongitudeDepart;
                commandeExistante.DistanceKm = commande.DistanceKm;
                commandeExistante.DureeEstimeeMinutes = commande.DureeEstimeeMinutes;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Commande modifiée avec succès.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Le statut n'est pas modifiable dans le formulaire : on réaffiche celui de la base.
            commande.Statut = commandeExistante.Statut;
            commande.DateCommande = commandeExistante.DateCommande;
            await PreparerFormulaireCommandeAsync(commande.ClientId);
            return View(commande);
        }

        // POST: Commande/Annuler/5
        // Une commande ne peut être annulée que tant qu'aucun colis n'est affecté ni parti.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Annuler(int id)
        {
            var commande = await _context.Commandes.FirstOrDefaultAsync(c => c.Id == id);
            if (commande == null) return NotFound();

            if (!await PeutConsulterCommandeAsync(commande))
                return Forbid();

            if (!await PeutEtreAnnuleeAsync(commande))
            {
                TempData["Error"] = "Cette commande ne peut plus être annulée : un colis est déjà affecté ou en route.";
                return RedirectToAction(nameof(Details), new { id });
            }

            commande.Statut = StatutCommande.Annulee;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"La commande CMD-{commande.Id} a été annulée.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<bool> PeutConsulterCommandeAsync(Commande commande)
        {
            if (User.EstPersonnel())
                return true;

            var client = await _compteClient.GetClientConnecteAsync(User);
            return client != null && commande.ClientId == client.Id;
        }

        private async Task<bool> PeutEtreAnnuleeAsync(Commande commande)
        {
            if (commande.Statut != StatutCommande.EnAttente)
                return false;

            var livraisonActive = await _context.Livraisons.AnyAsync(l =>
                l.Colis!.CommandeId == commande.Id
                && (l.Statut == StatutLivraison.EnAttente || l.Statut == StatutLivraison.EnCours));
            return !livraisonActive;
        }

        private async Task<Client?> ValiderClientAsync(int clientId, int? clientActuelId)
        {
            var client = await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
            if (client == null)
                ModelState.AddModelError(nameof(Commande.ClientId), "Sélectionnez un supermarché valide.");
            else if (!client.EstActif && client.Id != clientActuelId)
                ModelState.AddModelError(nameof(Commande.ClientId), "Ce supermarché est désactivé.");
            return client;
        }

        /// <summary>
        /// Calcule côté serveur le point de départ, la distance et la durée : le navigateur n'envoie que le point de livraison.
        /// Sans point sur la carte (JavaScript désactivé, carte non chargée), le centre d'une ville connue est utilisé.
        /// </summary>
        private async Task DefinirItineraireAsync(Commande commande, Client? client)
        {
            if (commande.LatitudeDestination is null || commande.LongitudeDestination is null)
            {
                var centreVille = _geolocalisation.CoordonneesVille(commande.VilleDestination);
                if (centreVille is null)
                {
                    ModelState.AddModelError(nameof(Commande.LatitudeDestination),
                        "Placez le point de livraison sur la carte pour calculer la distance.");
                    return;
                }
                commande.LatitudeDestination = centreVille.Value.Latitude;
                commande.LongitudeDestination = centreVille.Value.Longitude;
            }

            var destination = new PointGps(commande.LatitudeDestination.Value, commande.LongitudeDestination.Value);
            if (!GeolocalisationService.EstDansZoneCouverte(destination.Latitude, destination.Longitude))
            {
                ModelState.AddModelError(nameof(Commande.LatitudeDestination), "Le point de livraison doit se trouver en RDC.");
                return;
            }

            var depart = _geolocalisation.PointDeDepart(client);
            var itineraire = await _geolocalisation.CalculerItineraireAsync(depart.Point, destination, HttpContext.RequestAborted);
            commande.LatitudeDepart = depart.Point.Latitude;
            commande.LongitudeDepart = depart.Point.Longitude;
            commande.DistanceKm = itineraire.DistanceKm;
            commande.DureeEstimeeMinutes = itineraire.DureeMinutes;
        }

        private void NettoyerChamps(Commande commande)
        {
            commande.VilleDestination = NormaliserVille(commande.VilleDestination);
            commande.Description = Nettoyer(commande.Description);
            commande.AdresseDestination = Nettoyer(commande.AdresseDestination);
            commande.QuartierDestination = Nettoyer(commande.QuartierDestination);
            commande.ContactDestination = Nettoyer(commande.ContactDestination);
            commande.TelephoneDestination = Nettoyer(commande.TelephoneDestination);
        }

        private static string? Nettoyer(string? valeur)
            => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();

        /// <summary>« goma » devient « Goma » : on reprend l'orthographe d'une ville connue, sinon on met une majuscule.</summary>
        private string NormaliserVille(string? ville)
        {
            ville = ville?.Trim() ?? string.Empty;
            if (ville.Length == 0)
                return ville;

            var villeConnue = _geolocalisation.Villes.Keys
                .FirstOrDefault(v => StatutHelper.MemeVille(v, ville));
            return villeConnue ?? char.ToUpperInvariant(ville[0]) + ville[1..];
        }

        private async Task PreparerFormulaireCommandeAsync(int? clientId = null)
        {
            var estClient = User.IsInRole("Client");
            ViewBag.EstClient = estClient;

            Client? client;
            if (estClient)
            {
                client = await _compteClient.GetClientConnecteAsync(User);
                ViewBag.ClientConnecte = client;
            }
            else
            {
                // Le client actuel reste dans la liste même s'il a été désactivé depuis.
                var clients = await _context.Clients
                    .AsNoTracking()
                    .Where(c => c.EstActif || c.Id == clientId)
                    .OrderBy(c => c.NomSupermarche)
                    .ToListAsync();
                ViewBag.Clients = new SelectList(clients, "Id", "NomSupermarche", clientId);
                client = clients.FirstOrDefault(c => c.Id == clientId);
            }

            // La carte s'ouvre sur le supermarché expéditeur (ou le dépôt) tant qu'aucun point n'est choisi.
            var depart = _geolocalisation.PointDeDepart(client);
            ViewBag.CentreCarte = CarteHelper.Coordonnees(depart.Point.Latitude, depart.Point.Longitude);
        }
    }
}
