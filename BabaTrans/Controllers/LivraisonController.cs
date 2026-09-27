using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur,Agent,Livreur")]
    public class LivraisonController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly UserManager<Utilisateur> _userManager;

        public LivraisonController(BabaTransContext context, UserManager<Utilisateur> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Livraison
        public async Task<IActionResult> Index()
        {
            var query = _context.Livraisons
                .Include(l => l.Colis).ThenInclude(c => c!.Commande).ThenInclude(cmd => cmd!.Client)
                .Include(l => l.Livreur)
                .Include(l => l.MoyenTransport)
                .AsQueryable();

            if (User.IsInRole("Livreur"))
            {
                var currentUserId = _userManager.GetUserId(User);
                query = query.Where(l => l.LivreurId == currentUserId);
            }

            var livraisons = await query
                .OrderByDescending(l => l.Id)
                .ToListAsync();

            return View(livraisons);
        }

        // GET: Livraison/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var livraison = await _context.Livraisons
                .Include(l => l.Colis).ThenInclude(c => c!.Commande).ThenInclude(cmd => cmd!.Client)
                .Include(l => l.Colis).ThenInclude(c => c!.TimbreQRCode)
                .Include(l => l.Livreur)
                .Include(l => l.MoyenTransport)
                .FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!PeutGererLivraison(livraison))
                return Forbid();
            return View(livraison);
        }

        // GET: Livraison/Create
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Create()
        {
            await PreparerFormulaireLivraisonAsync();
            return View();
        }

        // POST: Livraison/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Create([Bind("ColisId,LivreurId,MoyenTransportId,Commentaire")] Livraison livraison)
        {
            var colis = await _context.Colis
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == livraison.ColisId);
            if (colis == null)
                ModelState.AddModelError(nameof(livraison.ColisId), "Le colis sélectionné n'existe pas.");
            else if (colis.Statut != StatutColis.QRCodeGenere && colis.Statut != StatutColis.Enregistre)
                ModelState.AddModelError(nameof(livraison.ColisId), "Ce colis ne peut plus être affecté à une livraison.");

            var affectationExistante = await _context.Livraisons
                .AnyAsync(l => l.ColisId == livraison.ColisId && l.Statut != StatutLivraison.Echouee);
            if (affectationExistante)
                ModelState.AddModelError(nameof(livraison.ColisId), "Ce colis est déjà affecté à une livraison active.");

            var livreur = string.IsNullOrWhiteSpace(livraison.LivreurId)
                ? null
                : await _userManager.FindByIdAsync(livraison.LivreurId);
            if (livreur == null || !livreur.EstActif || !await _userManager.IsInRoleAsync(livreur, "Livreur"))
                ModelState.AddModelError(nameof(livraison.LivreurId), "Le livreur sélectionné est invalide ou inactif.");

            // Validation du moyen de transport
            if (livraison.MoyenTransportId.HasValue)
            {
                var moyen = await _context.MoyensTransport.FindAsync(livraison.MoyenTransportId.Value);
                if (moyen == null || !moyen.EstDisponible)
                    ModelState.AddModelError(nameof(livraison.MoyenTransportId), "Le moyen de transport sélectionné est invalide ou indisponible.");
            }

            if (ModelState.IsValid)
            {
                livraison.Statut = StatutLivraison.EnAttente;
                _context.Add(livraison);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Livraison affectée avec le moyen de transport. Le colis passera en transit au scan de départ.";
                return RedirectToAction(nameof(Index));
            }
            await PreparerFormulaireLivraisonAsync(livraison.ColisId, livraison.LivreurId, livraison.MoyenTransportId);
            return View(livraison);
        }

        // POST: Livraison/ScannerDepart/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Livreur,Agent,Administrateur")]
        public async Task<IActionResult> ScannerDepart(int id)
        {
            var livraison = await _context.Livraisons.Include(l => l.Colis).ThenInclude(c => c!.Commande).FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!PeutGererLivraison(livraison))
                return Forbid();
            if (livraison.Statut != StatutLivraison.EnAttente || livraison.Colis == null
                || (livraison.Colis.Statut != StatutColis.QRCodeGenere && livraison.Colis.Statut != StatutColis.Enregistre))
            {
                TempData["Error"] = "Cette livraison ne peut pas encore être démarrée.";
                return RedirectToAction(nameof(Details), new { id });
            }

            livraison.DateDepart = DateTime.Now;
            livraison.Statut = StatutLivraison.EnCours;

            if (livraison.Colis != null)
            {
                livraison.Colis.Statut = StatutColis.EnTransit;
                if (livraison.Colis.Commande != null)
                    livraison.Colis.Commande.Statut = StatutCommande.EnCours;
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Scan de départ effectué. Livraison en cours.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Livraison/ScannerArrivee/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Livreur,Agent,Administrateur")]
        public async Task<IActionResult> ScannerArrivee(int id)
        {
            var livraison = await _context.Livraisons.Include(l => l.Colis).FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!PeutGererLivraison(livraison))
                return Forbid();
            if (livraison.Statut != StatutLivraison.EnCours || !livraison.DateDepart.HasValue)
            {
                TempData["Error"] = "Le scan de départ doit précéder le scan d'arrivée.";
                return RedirectToAction(nameof(Details), new { id });
            }

            livraison.DateArrivee = DateTime.Now;
            if (livraison.Colis != null)
                livraison.Colis.Statut = StatutColis.Arrive;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Scan d'arrivée effectué. Colis arrivé à destination.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Livraison/Confirmer/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Livreur,Agent,Administrateur")]
        public async Task<IActionResult> Confirmer(int id, string? commentaire)
        {
            var livraison = await _context.Livraisons
                .Include(l => l.Colis).ThenInclude(c => c!.Commande)
                .FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!PeutGererLivraison(livraison))
                return Forbid();
            if (livraison.Statut != StatutLivraison.EnCours || !livraison.DateArrivee.HasValue)
            {
                TempData["Error"] = "Le scan d'arrivée doit précéder la confirmation de livraison.";
                return RedirectToAction(nameof(Details), new { id });
            }

            livraison.DateLivraison = DateTime.Now;
            livraison.Statut = StatutLivraison.Livree;
            livraison.Commentaire = commentaire;

            if (livraison.Colis != null)
            {
                livraison.Colis.Statut = StatutColis.Livre;

                // Vérifier si tous les colis de la commande sont livrés
                if (livraison.Colis.Commande != null)
                {
                    var tousLivres = await _context.Colis
                        .Where(c => c.CommandeId == livraison.Colis.CommandeId)
                        .AllAsync(c => c.Statut == StatutColis.Livre || c.Id == livraison.ColisId);

                    if (tousLivres)
                        livraison.Colis.Commande.Statut = StatutCommande.Livree;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Livraison confirmée avec succès !";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task PreparerFormulaireLivraisonAsync(int? colisId = null, string? livreurId = null, int? moyenTransportId = null)
        {
            var colisAffectes = _context.Livraisons
                .Where(l => l.Statut != StatutLivraison.Echouee)
                .Select(l => l.ColisId);
            var colisDisponibles = await _context.Colis
                .Include(c => c.Commande)
                .Where(c => (c.Statut == StatutColis.QRCodeGenere || c.Statut == StatutColis.Enregistre)
                    && (!colisAffectes.Contains(c.Id) || c.Id == colisId))
                .ToListAsync();
            var livreurs = await _userManager.GetUsersInRoleAsync("Livreur");
            var moyensTransport = await _context.MoyensTransport
                .Where(m => m.EstDisponible)
                .ToListAsync();

            ViewBag.Colis = new SelectList(
                colisDisponibles.Select(c => new { c.Id, Display = $"Colis #{c.Id} - {c.Description} ({c.CodeSuivi})" }),
                "Id", "Display", colisId);
            ViewBag.Livreurs = new SelectList(
                livreurs.Where(l => l.EstActif).Select(l => new { l.Id, Display = $"{l.Prenom} {l.Nom} ({l.Immatriculation})" }),
                "Id", "Display", livreurId);
            ViewBag.MoyensTransport = new SelectList(
                moyensTransport.Select(m => new { m.Id, Display = $"{m.Type} - {m.Immatriculation ?? "N/A"} (Capacité: {m.Capacite} kg)" }),
                "Id", "Display", moyenTransportId);
        }

        private bool PeutGererLivraison(Livraison livraison)
        {
            if (User.IsInRole("Administrateur") || User.IsInRole("Agent"))
                return true;

            var currentUserId = _userManager.GetUserId(User);
            return livraison.LivreurId == currentUserId;
        }
    }
}
