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

        public CommandeController(BabaTransContext context, TarificationService tarificationService, CompteClientService compteClient)
        {
            _context = context;
            _tarificationService = tarificationService;
            _compteClient = compteClient;
        }

        public async Task<IActionResult> Index()
        {
            var query = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Trajet)
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
                .Include(c => c.Trajet).ThenInclude(t => t!.MoyenTransport)
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
        public async Task<IActionResult> Create([Bind("VilleDestination,Description,PoidsEstimeKg,AdresseDestination,QuartierDestination,ContactDestination,TelephoneDestination,ClientId,TrajetId")] Commande commande)
        {
            if (User.IsInRole("Client"))
            {
                var client = await _compteClient.GetClientConnecteAsync(User);
                if (client == null || !client.EstActif)
                {
                    TempData["Error"] = "Aucun supermarché actif n'est associé à ce compte. Contactez BABA-Trans.";
                    return RedirectToAction(nameof(Index));
                }

                // Un client ne choisit ni le supermarché ni le trajet : c'est le rôle de l'agent.
                commande.ClientId = client.Id;
                commande.TrajetId = null;
                ModelState.Remove(nameof(commande.ClientId));
                ModelState.Remove(nameof(commande.TrajetId));
            }
            else
            {
                await ValiderClientAsync(commande.ClientId, clientActuelId: null);
            }

            NettoyerChamps(commande);

            if (!commande.PoidsEstimeKg.HasValue || commande.PoidsEstimeKg <= 0)
                ModelState.AddModelError(nameof(commande.PoidsEstimeKg), "Le poids estimé est obligatoire pour calculer le tarif.");

            await ValiderTrajetAsync(commande);

            if (ModelState.IsValid)
            {
                commande.DateCommande = DateTime.Now;
                commande.Statut = StatutCommande.EnAttente;
                _context.Add(commande);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Commande CMD-{commande.Id} créée avec succès.";
                return RedirectToAction(nameof(Details), new { id = commande.Id });
            }

            await PreparerFormulaireCommandeAsync(commande.ClientId, commande.TrajetId);
            return View(commande);
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

            await PreparerFormulaireCommandeAsync(commande.ClientId, commande.TrajetId);
            return View(commande);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,VilleDestination,Description,PoidsEstimeKg,AdresseDestination,QuartierDestination,ContactDestination,TelephoneDestination,ClientId,TrajetId")] Commande commande)
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

            await ValiderClientAsync(commande.ClientId, clientActuelId: commandeExistante.ClientId);
            NettoyerChamps(commande);
            await ValiderTrajetAsync(commande);

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
                commandeExistante.TrajetId = commande.TrajetId;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Commande modifiée avec succès.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Le statut n'est pas modifiable dans le formulaire : on réaffiche celui de la base.
            commande.Statut = commandeExistante.Statut;
            commande.DateCommande = commandeExistante.DateCommande;
            await PreparerFormulaireCommandeAsync(commande.ClientId, commande.TrajetId);
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

        private async Task ValiderClientAsync(int clientId, int? clientActuelId)
        {
            var client = await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
            if (client == null)
                ModelState.AddModelError(nameof(Commande.ClientId), "Sélectionnez un supermarché valide.");
            else if (!client.EstActif && client.Id != clientActuelId)
                ModelState.AddModelError(nameof(Commande.ClientId), "Ce supermarché est désactivé.");
        }

        /// <summary>Le trajet affecté doit arriver dans la ville de destination de la commande.</summary>
        private async Task ValiderTrajetAsync(Commande commande)
        {
            if (!commande.TrajetId.HasValue)
                return;

            var trajet = await _context.Trajets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == commande.TrajetId);
            if (trajet == null)
                ModelState.AddModelError(nameof(Commande.TrajetId), "Le trajet sélectionné n'existe pas.");
            else if (!string.IsNullOrWhiteSpace(commande.VilleDestination)
                     && !StatutHelper.MemeVille(trajet.VilleArrivee, commande.VilleDestination))
                ModelState.AddModelError(nameof(Commande.TrajetId),
                    $"Ce trajet arrive à {trajet.VilleArrivee}, alors que la commande est destinée à {commande.VilleDestination}.");
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

            var villeConnue = _tarificationService.LireParametres().SupplementsDestination.Keys
                .FirstOrDefault(v => StatutHelper.MemeVille(v, ville));
            return villeConnue ?? char.ToUpperInvariant(ville[0]) + ville[1..];
        }

        private async Task PreparerFormulaireCommandeAsync(int? clientId = null, int? trajetId = null)
        {
            var estClient = User.IsInRole("Client");
            ViewBag.EstClient = estClient;
            ViewBag.ParametresTarification = _tarificationService.LireParametres();

            if (estClient)
            {
                ViewBag.ClientConnecte = await _compteClient.GetClientConnecteAsync(User);
                return;
            }

            // Le client actuel reste dans la liste même s'il a été désactivé depuis.
            ViewBag.Clients = new SelectList(
                await _context.Clients
                    .Where(c => c.EstActif || c.Id == clientId)
                    .OrderBy(c => c.NomSupermarche)
                    .ToListAsync(),
                "Id", "NomSupermarche", clientId);

            var trajets = await _context.Trajets
                .OrderBy(t => t.VilleDepart).ThenBy(t => t.VilleArrivee)
                .Select(t => new { t.Id, Display = t.VilleDepart + " → " + t.VilleArrivee })
                .ToListAsync();
            ViewBag.Trajets = new SelectList(trajets, "Id", "Display", trajetId);
        }
    }
}
