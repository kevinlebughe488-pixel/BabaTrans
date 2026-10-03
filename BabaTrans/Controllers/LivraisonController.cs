using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Helpers;
using BabaTrans.Models;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur,Agent,Livreur")]
    public class LivraisonController : Controller
    {
        private const int LongueurMaxCommentaire = 500;

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

            // Les tournées à traiter d'abord (en attente, en cours), puis l'historique.
            var livraisons = await query
                .OrderBy(l => l.Statut == StatutLivraison.Livree || l.Statut == StatutLivraison.Echouee)
                .ThenByDescending(l => l.Id)
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
        public async Task<IActionResult> Create(int? colisId)
        {
            await PreparerFormulaireLivraisonAsync(colisId);
            return View(new Livraison { ColisId = colisId ?? 0 });
        }

        // POST: Livraison/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Create([Bind("ColisId,LivreurId,MoyenTransportId,Commentaire")] Livraison livraison)
        {
            var colis = await _context.Colis
                .AsNoTracking()
                .Include(c => c.Commande)
                .FirstOrDefaultAsync(c => c.Id == livraison.ColisId);
            if (colis == null)
                ModelState.AddModelError(nameof(livraison.ColisId), "Le colis sélectionné n'existe pas.");
            else if (!colis.Statut.EstAffectable())
                ModelState.AddModelError(nameof(livraison.ColisId), "Ce colis ne peut plus être affecté à une livraison.");
            else if (colis.Commande == null || !colis.Commande.Statut.EstOuverte())
                ModelState.AddModelError(nameof(livraison.ColisId), "La commande de ce colis est clôturée ou annulée.");

            var affectationExistante = await _context.Livraisons
                .AnyAsync(l => l.ColisId == livraison.ColisId
                    && (l.Statut == StatutLivraison.EnAttente || l.Statut == StatutLivraison.EnCours));
            if (affectationExistante)
                ModelState.AddModelError(nameof(livraison.ColisId), "Ce colis est déjà affecté à une livraison active.");

            var livreur = string.IsNullOrWhiteSpace(livraison.LivreurId)
                ? null
                : await _userManager.FindByIdAsync(livraison.LivreurId);
            if (livreur == null || !livreur.EstActif || !await _userManager.IsInRoleAsync(livreur, "Livreur"))
                ModelState.AddModelError(nameof(livraison.LivreurId), "Le livreur sélectionné est invalide ou inactif.");

            if (livraison.MoyenTransportId.HasValue)
            {
                var moyen = await _context.MoyensTransport.FindAsync(livraison.MoyenTransportId.Value);
                if (moyen == null || !moyen.EstDisponible)
                    ModelState.AddModelError(nameof(livraison.MoyenTransportId), "Le moyen de transport sélectionné est invalide ou indisponible.");
                else if (colis != null && colis.Poids > moyen.Capacite)
                    ModelState.AddModelError(nameof(livraison.MoyenTransportId), $"Le colis ({colis.Poids} kg) dépasse la capacité du véhicule ({moyen.Capacite} kg).");
            }
            else
            {
                ModelState.AddModelError(nameof(livraison.MoyenTransportId), "Un moyen de transport est obligatoire pour affecter une livraison.");
            }

            if (ModelState.IsValid)
            {
                livraison.Statut = StatutLivraison.EnAttente;
                livraison.Commentaire = string.IsNullOrWhiteSpace(livraison.Commentaire) ? null : livraison.Commentaire.Trim();
                _context.Add(livraison);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Livraison affectée. Le colis passera en transit au scan de départ.";
                return RedirectToAction(nameof(Details), new { id = livraison.Id });
            }
            await PreparerFormulaireLivraisonAsync(livraison.ColisId, livraison.LivreurId, livraison.MoyenTransportId);
            return View(livraison);
        }

        // POST: Livraison/ScannerDepart/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScannerDepart(int id)
        {
            var livraison = await _context.Livraisons
                .Include(l => l.Colis).ThenInclude(c => c!.Commande)
                .FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!PeutGererLivraison(livraison))
                return Forbid();
            if (livraison.Statut != StatutLivraison.EnAttente || livraison.Colis == null
                || !livraison.Colis.Statut.EstAffectable()
                || livraison.Colis.Commande == null || !livraison.Colis.Commande.Statut.EstOuverte())
            {
                TempData["Error"] = "Cette livraison ne peut pas être démarrée (colis déjà parti ou commande clôturée).";
                return RedirectToAction(nameof(Details), new { id });
            }

            livraison.DateDepart = DateTime.Now;
            livraison.Statut = StatutLivraison.EnCours;
            livraison.Colis.Statut = StatutColis.EnTransit;
            livraison.Colis.Commande.Statut = StatutCommande.EnCours;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Scan de départ effectué. Livraison en cours.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Livraison/ScannerArrivee/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScannerArrivee(int id)
        {
            var livraison = await _context.Livraisons.Include(l => l.Colis).FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!PeutGererLivraison(livraison))
                return Forbid();
            if (livraison.Statut != StatutLivraison.EnCours || !livraison.DateDepart.HasValue || livraison.DateArrivee.HasValue)
            {
                TempData["Error"] = "Le scan d'arrivée n'est possible qu'une fois, après le scan de départ.";
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
            if (string.IsNullOrWhiteSpace(commentaire))
            {
                TempData["Error"] = "Indiquez le nom du signataire pour confirmer la remise.";
                return RedirectToAction(nameof(Details), new { id });
            }

            livraison.DateLivraison = DateTime.Now;
            livraison.Statut = StatutLivraison.Livree;
            // On conserve la note saisie à l'affectation et on y ajoute la preuve de remise.
            livraison.Commentaire = AjouterNote(livraison.Commentaire, $"Remise : {commentaire.Trim()}");

            if (livraison.Colis != null)
            {
                livraison.Colis.Statut = StatutColis.Livre;

                // Le colis courant n'est pas encore enregistré comme livré en base : on l'exclut du contrôle.
                if (livraison.Colis.Commande != null)
                {
                    var tousLivres = await _context.Colis
                        .Where(c => c.CommandeId == livraison.Colis.CommandeId && c.Id != livraison.ColisId)
                        .AllAsync(c => c.Statut == StatutColis.Livre);

                    if (tousLivres)
                        livraison.Colis.Commande.Statut = StatutCommande.Livree;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = livraison.Colis?.Commande?.Statut == StatutCommande.Livree
                ? "Livraison confirmée. Tous les colis sont livrés : la commande est clôturée."
                : "Livraison confirmée avec succès.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Livraison/Echec/5
        // Déclare un échec (client absent, adresse introuvable, incident...). Le colis redevient affectable.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Echec(int id, string? motif)
        {
            var livraison = await _context.Livraisons
                .Include(l => l.Colis).ThenInclude(c => c!.Commande)
                .Include(l => l.Colis).ThenInclude(c => c!.TimbreQRCode)
                .FirstOrDefaultAsync(l => l.Id == id);
            if (livraison == null) return NotFound();
            if (!PeutGererLivraison(livraison))
                return Forbid();
            if (livraison.Statut != StatutLivraison.EnAttente && livraison.Statut != StatutLivraison.EnCours)
            {
                TempData["Error"] = "Seule une livraison en attente ou en cours peut être déclarée en échec.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (string.IsNullOrWhiteSpace(motif))
            {
                TempData["Error"] = "Indiquez le motif de l'échec.";
                return RedirectToAction(nameof(Details), new { id });
            }

            livraison.Statut = StatutLivraison.Echouee;
            livraison.Commentaire = AjouterNote(livraison.Commentaire, $"Échec : {motif.Trim()}");

            if (livraison.Colis != null)
            {
                livraison.Colis.Statut = livraison.Colis.TimbreQRCode != null ? StatutColis.QRCodeGenere : StatutColis.Enregistre;

                // Si plus aucun colis de la commande n'est en route ni livré, la commande revient en attente.
                var commande = livraison.Colis.Commande;
                if (commande != null && commande.Statut == StatutCommande.EnCours)
                {
                    var autreColisAvance = await _context.Colis.AnyAsync(c =>
                        c.CommandeId == commande.Id && c.Id != livraison.ColisId
                        && (c.Statut == StatutColis.EnTransit || c.Statut == StatutColis.Arrive || c.Statut == StatutColis.Livre));
                    if (!autreColisAvance)
                        commande.Statut = StatutCommande.EnAttente;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Échec enregistré. Le colis peut être réaffecté à une nouvelle livraison.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private static string AjouterNote(string? existant, string note)
        {
            var texte = string.IsNullOrWhiteSpace(existant) ? note : $"{existant.Trim()}\n{note}";
            return texte.Length <= LongueurMaxCommentaire ? texte : texte[..LongueurMaxCommentaire];
        }

        private async Task PreparerFormulaireLivraisonAsync(int? colisId = null, string? livreurId = null, int? moyenTransportId = null)
        {
            var colisAffectes = _context.Livraisons
                .Where(l => l.Statut == StatutLivraison.EnAttente || l.Statut == StatutLivraison.EnCours)
                .Select(l => l.ColisId);
            var colisDisponibles = await _context.Colis
                .Include(c => c.Commande)
                .Where(c => (c.Statut == StatutColis.QRCodeGenere || c.Statut == StatutColis.Enregistre)
                    && (c.Commande!.Statut == StatutCommande.EnAttente || c.Commande.Statut == StatutCommande.EnCours)
                    && !colisAffectes.Contains(c.Id))
                .OrderBy(c => c.Id)
                .ToListAsync();
            var livreurs = await _userManager.GetUsersInRoleAsync("Livreur");
            var moyensTransport = await _context.MoyensTransport
                .Where(m => m.EstDisponible)
                .OrderBy(m => m.Type)
                .ToListAsync();

            ViewBag.Colis = new SelectList(
                colisDisponibles.Select(c => new
                {
                    c.Id,
                    Display = $"Colis #{c.Id} · {c.CodeSuivi} · {c.Poids} kg → {c.Commande?.VilleDestination}"
                }),
                "Id", "Display", colisId);
            ViewBag.Livreurs = new SelectList(
                livreurs.Where(l => l.EstActif)
                    .OrderBy(l => l.Nom)
                    .Select(l => new
                    {
                        l.Id,
                        Display = string.IsNullOrWhiteSpace(l.Immatriculation)
                            ? $"{l.Prenom} {l.Nom}"
                            : $"{l.Prenom} {l.Nom} ({l.Immatriculation})"
                    }),
                "Id", "Display", livreurId);
            ViewBag.MoyensTransport = new SelectList(
                moyensTransport.Select(m => new { m.Id, Display = $"{m.Type} · {m.Immatriculation ?? "sans immatriculation"} · capacité {m.Capacite:N0} kg" }),
                "Id", "Display", moyenTransportId);
        }

        private bool PeutGererLivraison(Livraison livraison)
        {
            if (User.EstPersonnel())
                return true;

            var currentUserId = _userManager.GetUserId(User);
            return livraison.LivreurId == currentUserId;
        }
    }
}
