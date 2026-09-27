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
    [Authorize]
    public class ColisController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly QRCodeService _qrCodeService;
        private readonly UserManager<Utilisateur> _userManager;

        public ColisController(BabaTransContext context, QRCodeService qrCodeService, UserManager<Utilisateur> userManager)
        {
            _context = context;
            _qrCodeService = qrCodeService;
            _userManager = userManager;
        }

        // GET: Colis
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Index()
        {
            var colis = await _context.Colis
                .Include(c => c.Commande).ThenInclude(cmd => cmd!.Client)
                .Include(c => c.TimbreQRCode)
                .OrderByDescending(c => c.DateEnregistrement)
                .ToListAsync();
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
            return View(colis);
        }

        // GET: Colis/Create
        [Authorize(Roles = "Administrateur,Agent")]
        public IActionResult Create()
        {
            ViewBag.Commandes = new SelectList(
                _context.Commandes.Include(c => c.Client).Select(c => new { c.Id, Display = $"CMD-{c.Id} - {c.Client!.NomSupermarche}" }),
                "Id", "Display");
            return View();
        }

        // POST: Colis/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur,Agent")]
        public async Task<IActionResult> Create([Bind("Description,Poids,CommandeId")] Colis colis)
        {
            if (ModelState.IsValid)
            {
                // Générer le code de suivi unique
                colis.CodeSuivi = $"BT-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
                colis.DateEnregistrement = DateTime.Now;
                colis.Statut = StatutColis.Enregistre;

                _context.Add(colis);
                await _context.SaveChangesAsync();

                // Générer le QR-Code automatiquement
                var contenu = _qrCodeService.GenererContenuColis(colis.Id, colis.CodeSuivi, colis.Description);
                var imageBase64 = _qrCodeService.GenererQRCode(contenu);

                var timbre = new TimbreQRCode
                {
                    ColisId = colis.Id,
                    Contenu = contenu,
                    ImageBase64 = imageBase64,
                    DateGeneration = DateTime.Now
                };
                _context.TimbresQRCode.Add(timbre);

                colis.Statut = StatutColis.QRCodeGenere;
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Colis enregistré avec succès. Code de suivi : {colis.CodeSuivi}";
                return RedirectToAction(nameof(Details), new { id = colis.Id });
            }
            ViewBag.Commandes = new SelectList(
                _context.Commandes.Include(c => c.Client).Select(c => new { c.Id, Display = $"CMD-{c.Id} - {c.Client!.NomSupermarche}" }),
                "Id", "Display", colis.CommandeId);
            return View(colis);
        }

        // GET: Colis/Suivi (Accès sécurisé)
        [AllowAnonymous]
        public async Task<IActionResult> Suivi(string? code)
        {
            ViewModels.SuiviColisViewModel model = new() { CodeSuivi = code };

            if (!string.IsNullOrEmpty(code))
            {
                var colis = await _context.Colis
                    .Include(c => c.Commande).ThenInclude(cmd => cmd!.Client)
                    .Include(c => c.TimbreQRCode)
                    .Include(c => c.Livraisons).ThenInclude(l => l.Livreur)
                    .Include(c => c.Livraisons).ThenInclude(l => l.MoyenTransport)
                    .FirstOrDefaultAsync(c => c.CodeSuivi == code);

                if (colis == null)
                {
                    ModelState.AddModelError(string.Empty, "Aucun colis trouvé avec ce code.");
                    model.Recherche = true;
                    return View(model);
                }

                // Vérification de sécurité pour le rôle Client
                if (User.IsInRole("Client"))
                {
                    var user = await _userManager.GetUserAsync(User);
                    var email = user?.Email ?? User.Identity?.Name;
                    var client = await _context.Clients
                        .FirstOrDefaultAsync(c => c.Email == email);

                    if (client == null || colis.Commande?.ClientId != client.Id)
                    {
                        return Forbid(); // Le client n'est pas propriétaire de ce colis
                    }
                }

                if (colis.TimbreQRCode != null && !_qrCodeService.VerifierContenuColis(colis.TimbreQRCode.Contenu))
                    colis.TimbreQRCode.ImageBase64 = null;

                model.Recherche = true;
                model.Colis = colis;
            }
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

            if (colis.TimbreQRCode != null)
            {
                _context.TimbresQRCode.Remove(colis.TimbreQRCode);
            }

            var contenu = _qrCodeService.GenererContenuColis(colis.Id, colis.CodeSuivi ?? $"BT-{colis.Id}", colis.Description);
            var imageBase64 = _qrCodeService.GenererQRCode(contenu);

            var timbre = new TimbreQRCode
            {
                ColisId = colis.Id,
                Contenu = contenu,
                ImageBase64 = imageBase64,
                DateGeneration = DateTime.Now
            };
            _context.TimbresQRCode.Add(timbre);
            colis.Statut = StatutColis.QRCodeGenere;
            await _context.SaveChangesAsync();

            TempData["Success"] = "QR-Code régénéré avec succès.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
