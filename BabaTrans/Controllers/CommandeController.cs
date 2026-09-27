using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;
using BabaTrans.Services;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur,Agent,Client")]
    public class CommandeController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly UserManager<Utilisateur> _userManager;
        private readonly TarificationService _tarificationService;

        public CommandeController(BabaTransContext context, UserManager<Utilisateur> userManager, TarificationService tarificationService)
        {
            _context = context;
            _userManager = userManager;
            _tarificationService = tarificationService;
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
                var client = await GetClientDuCompteAsync();
                if (client == null)
                    return View(new List<Commande>());

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
                .Include(c => c.Colis).ThenInclude(col => col.TimbreQRCode)
                .Include(c => c.Colis).ThenInclude(col => col.Livraisons)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (commande == null) return NotFound();

            if (!await PeutConsulterCommandeAsync(commande))
                return Forbid();

            return View(commande);
        }

        [Authorize(Roles = "Administrateur,Agent,Client")]
        public async Task<IActionResult> Create()
        {
            await PreparerFormulaireCommandeAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent,Client")]
        public async Task<IActionResult> Create([Bind("VilleDestination,Description,PoidsEstimeKg,AdresseDestination,QuartierDestination,ContactDestination,TelephoneDestination,ClientId,TrajetId")] Commande commande)
        {
            if (User.IsInRole("Client"))
            {
                var client = await GetClientDuCompteAsync();
                if (client == null)
                {
                    TempData["Error"] = "Aucun supermarché n'est associé à ce compte. Contactez BABA-Trans.";
                    return RedirectToAction(nameof(Index));
                }

                commande.ClientId = client.Id;
                commande.TrajetId = null;
                ModelState.Remove(nameof(commande.ClientId));
                ModelState.Remove(nameof(commande.TrajetId));
            }

            commande.Description = ConstruireDescriptionCommande(commande);

            if (User.IsInRole("Client") && (!commande.PoidsEstimeKg.HasValue || commande.PoidsEstimeKg <= 0))
                ModelState.AddModelError(nameof(commande.PoidsEstimeKg), "Indiquez le poids estimé du colis ou de l'expédition.");

            if (ModelState.IsValid)
            {
                commande.DateCommande = DateTime.Now;
                commande.Statut = StatutCommande.EnAttente;
                _context.Add(commande);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Commande créée avec succès.";
                return RedirectToAction(nameof(Index));
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
            await PreparerFormulaireCommandeAsync(commande.ClientId, commande.TrajetId);
            return View(commande);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,VilleDestination,Description,AdresseDestination,QuartierDestination,ContactDestination,TelephoneDestination,ClientId,TrajetId")] Commande commande)
        {
            if (id != commande.Id) return NotFound();

            commande.Description = ConstruireDescriptionCommande(commande);

            if (ModelState.IsValid)
            {
                try
                {
                    var commandeExistante = await _context.Commandes.FindAsync(id);
                    if (commandeExistante == null) return NotFound();

                    commandeExistante.VilleDestination = commande.VilleDestination;
                    commandeExistante.Description = commande.Description;
                    commandeExistante.ClientId = commande.ClientId;
                    commandeExistante.TrajetId = commande.TrajetId;
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Commande modifiée avec succès.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Commandes.Any(c => c.Id == id)) return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            await PreparerFormulaireCommandeAsync(commande.ClientId, commande.TrajetId);
            return View(commande);
        }

        private async Task<Client?> GetClientDuCompteAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            var email = user?.Email ?? User.Identity?.Name;
            if (string.IsNullOrEmpty(email))
                return null;

            return await _context.Clients.FirstOrDefaultAsync(c => c.Email == email);
        }

        private async Task<bool> PeutConsulterCommandeAsync(Commande commande)
        {
            if (User.IsInRole("Administrateur") || User.IsInRole("Agent"))
                return true;

            var client = await GetClientDuCompteAsync();
            return client != null && commande.ClientId == client.Id;
        }

        private static string ConstruireDescriptionCommande(Commande commande)
        {
            var details = new List<string>();

            if (!string.IsNullOrWhiteSpace(commande.AdresseDestination))
                details.Add($"Adresse: {commande.AdresseDestination}");

            if (!string.IsNullOrWhiteSpace(commande.QuartierDestination))
                details.Add($"Quartier: {commande.QuartierDestination}");

            if (!string.IsNullOrWhiteSpace(commande.ContactDestination))
                details.Add($"Contact: {commande.ContactDestination}");

            if (!string.IsNullOrWhiteSpace(commande.TelephoneDestination))
                details.Add($"Téléphone: {commande.TelephoneDestination}");

            if (!string.IsNullOrWhiteSpace(commande.Description))
                details.Add(commande.Description.Trim());

            return string.Join(" | ", details.Where(d => !string.IsNullOrWhiteSpace(d)));
        }

        private async Task PreparerFormulaireCommandeAsync(int? clientId = null, int? trajetId = null)
        {
            var estClient = User.IsInRole("Client");
            ViewBag.EstClient = estClient;
            ViewBag.ParametresTarification = _tarificationService.LireParametres();

            if (estClient)
            {
                var client = await GetClientDuCompteAsync();
                ViewBag.ClientConnecte = client;
            }
            else
            {
                ViewBag.Clients = new SelectList(
                    await _context.Clients.Where(c => c.EstActif).OrderBy(c => c.NomSupermarche).ToListAsync(),
                    "Id", "NomSupermarche", clientId);

                var trajets = await _context.Trajets
                    .Select(t => new { t.Id, Display = t.VilleDepart + " → " + t.VilleArrivee })
                    .ToListAsync();
                ViewBag.Trajets = new SelectList(trajets, "Id", "Display", trajetId);
            }
        }
    }
}
