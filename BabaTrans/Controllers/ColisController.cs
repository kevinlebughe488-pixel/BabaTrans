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
    [Authorize]
    public class ColisController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly QRCodeService _qrCodeService;
        private readonly CompteClientService _compteClient;

        public ColisController(BabaTransContext context, QRCodeService qrCodeService, CompteClientService compteClient)
        {
            _context = context;
            _qrCodeService = qrCodeService;
            _compteClient = compteClient;
        }

        // GET: Colis
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Index()
        {
            // Les images QR ne sont plus chargées ici : la liste affiche une miniature servie par l'action QRImage.
            var colis = await _context.Colis
                .Include(c => c.Commande).ThenInclude(cmd => cmd!.Client)
                .OrderByDescending(c => c.DateEnregistrement)
                .ToListAsync();

            ViewBag.ColisAvecTimbre = (await _context.TimbresQRCode
                .Select(t => t.ColisId)
                .ToListAsync()).ToHashSet();

            return View(colis);
        }

        // GET: Colis/Details/5
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var colis = await _context.Colis
                .Include(c => c.Commande).ThenInclude(cmd => cmd!.Client)
                .Include(c => c.TimbreQRCode)
                .Include(c => c.Livraisons).ThenInclude(l => l.Livreur)
                .Include(c => c.Livraisons).ThenInclude(l => l.MoyenTransport)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (colis == null) return NotFound();

            var livraisonActive = colis.Livraisons.Any(l =>
                l.Statut == StatutLivraison.EnAttente || l.Statut == StatutLivraison.EnCours);
            ViewBag.PeutAffecter = colis.Statut.EstAffectable()
                && !livraisonActive
                && colis.Commande != null
                && colis.Commande.Statut.EstOuverte();

            return View(colis);
        }

        // GET: Colis/QRImage/5 : image PNG du timbre (miniatures de la liste, impression).
        [Authorize(Roles = "Administrateur,Agent")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
        public async Task<IActionResult> QRImage(int id)
        {
            var imageBase64 = await _context.TimbresQRCode
                .Where(t => t.ColisId == id)
                .Select(t => t.ImageBase64)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(imageBase64))
                return NotFound();

            return File(Convert.FromBase64String(imageBase64), "image/png");
        }

        // GET: Colis/Create
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Create(int? commandeId)
        {
            // Pas de modèle : le champ poids reste vide (et non « 0 »), la commande est présélectionnée par la liste.
            await PreparerListeCommandesAsync(commandeId);
            return View();
        }

        // POST: Colis/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Create([Bind("Description,Poids,CommandeId")] Colis colis)
        {
            var commande = await _context.Commandes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == colis.CommandeId);
            if (commande == null)
                ModelState.AddModelError(nameof(colis.CommandeId), "Sélectionnez une commande valide.");
            else if (!commande.Statut.EstOuverte())
                ModelState.AddModelError(nameof(colis.CommandeId),
                    $"La commande CMD-{commande.Id} est {commande.Statut.Libelle().ToLowerInvariant()} : on ne peut plus y ajouter de colis.");

            if (ModelState.IsValid)
            {
                colis.Description = colis.Description.Trim();
                colis.CodeSuivi = $"BT-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
                colis.DateEnregistrement = DateTime.Now;
                colis.Statut = StatutColis.Enregistre;

                // Transaction : le colis et son timbre QR sont créés ensemble ou pas du tout.
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    _context.Add(colis);
                    await _context.SaveChangesAsync();

                    // La date d'enregistrement (fixe) est signée pour que la signature reste stable.
                    var contenu = _qrCodeService.GenererContenuColis(colis.Id, colis.CodeSuivi, colis.Description, colis.DateEnregistrement);
                    _context.TimbresQRCode.Add(new TimbreQRCode
                    {
                        ColisId = colis.Id,
                        Contenu = contenu,
                        ImageBase64 = _qrCodeService.GenererQRCode(contenu),
                        DateGeneration = DateTime.Now
                    });

                    colis.Statut = StatutColis.QRCodeGenere;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    TempData["Success"] = $"Colis enregistré avec succès. Code de suivi : {colis.CodeSuivi}";
                    return RedirectToAction(nameof(Details), new { id = colis.Id });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            await PreparerListeCommandesAsync(colis.CommandeId);
            return View(colis);
        }

        // GET: Colis/Suivi
        // Toute personne qui possède le code voit l'avancement du colis.
        // Les détails (contenu, supermarché, livreur, timbre) sont réservés au personnel et au supermarché propriétaire.
        [AllowAnonymous]
        public async Task<IActionResult> Suivi(string? code)
        {
            code = code?.Trim().ToUpperInvariant();
            var model = new ViewModels.SuiviColisViewModel { CodeSuivi = code };

            if (string.IsNullOrEmpty(code))
                return View(model);

            model.Recherche = true;
            var colis = await _context.Colis
                .Include(c => c.Commande).ThenInclude(cmd => cmd!.Client)
                .Include(c => c.TimbreQRCode)
                .Include(c => c.Livraisons).ThenInclude(l => l.Livreur)
                .Include(c => c.Livraisons).ThenInclude(l => l.MoyenTransport)
                .FirstOrDefaultAsync(c => c.CodeSuivi == code);

            if (colis == null)
                return View(model);

            if (User.EstPersonnel() || User.IsInRole("Livreur"))
            {
                model.AccesComplet = true;
            }
            else if (User.IsInRole("Client"))
            {
                var client = await _compteClient.GetClientConnecteAsync(User);
                model.AccesComplet = client != null && colis.Commande?.ClientId == client.Id;
            }

            // Un timbre dont la signature ne correspond plus n'est pas affiché.
            if (colis.TimbreQRCode != null && !_qrCodeService.VerifierContenuColis(colis.TimbreQRCode.Contenu))
                colis.TimbreQRCode.ImageBase64 = null;

            model.Colis = colis;
            model.Livraisons = colis.Livraisons.OrderByDescending(l => l.Id).ToList();
            return View(model);
        }

        // POST: Colis/GenererQRCode/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> GenererQRCode(int id)
        {
            var colis = await _context.Colis
                .Include(c => c.TimbreQRCode)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (colis == null) return NotFound();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (colis.TimbreQRCode != null)
                    _context.TimbresQRCode.Remove(colis.TimbreQRCode);

                var contenu = _qrCodeService.GenererContenuColis(colis.Id, colis.CodeSuivi ?? $"BT-{colis.Id}", colis.Description, colis.DateEnregistrement);
                _context.TimbresQRCode.Add(new TimbreQRCode
                {
                    ColisId = colis.Id,
                    Contenu = contenu,
                    ImageBase64 = _qrCodeService.GenererQRCode(contenu),
                    DateGeneration = DateTime.Now
                });

                // Régénérer le timbre ne doit pas faire reculer un colis déjà en route ou livré.
                if (colis.Statut == StatutColis.Enregistre)
                    colis.Statut = StatutColis.QRCodeGenere;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = "Timbre QR-Code généré avec succès.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>Seules les commandes encore ouvertes (en attente ou en cours) peuvent recevoir un colis.</summary>
        private async Task PreparerListeCommandesAsync(int? commandeId)
        {
            var commandes = await _context.Commandes
                .Where(c => c.Statut == StatutCommande.EnAttente || c.Statut == StatutCommande.EnCours)
                .OrderByDescending(c => c.DateCommande)
                .Select(c => new { c.Id, NomSupermarche = c.Client!.NomSupermarche, c.VilleDestination })
                .ToListAsync();

            ViewBag.Commandes = new SelectList(
                commandes.Select(c => new { c.Id, Display = $"CMD-{c.Id} · {c.NomSupermarche} → {c.VilleDestination}" }),
                "Id", "Display", commandeId);
        }
    }
}
