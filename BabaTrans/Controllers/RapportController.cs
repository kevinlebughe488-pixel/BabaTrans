using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur")]
    public class RapportController : Controller
    {
        private readonly BabaTransContext _context;

        public RapportController(BabaTransContext context)
        {
            _context = context;
        }

        // GET: Rapport
        public async Task<IActionResult> Index()
        {
            var rapport = new
            {
                TotalClients = await _context.Clients.CountAsync(),
                ClientsActifs = await _context.Clients.CountAsync(c => c.EstActif),
                TotalCommandes = await _context.Commandes.CountAsync(),
                CommandesEnAttente = await _context.Commandes.CountAsync(c => c.Statut == StatutCommande.EnAttente),
                CommandesEnCours = await _context.Commandes.CountAsync(c => c.Statut == StatutCommande.EnCours),
                CommandesLivrees = await _context.Commandes.CountAsync(c => c.Statut == StatutCommande.Livree),
                TotalColis = await _context.Colis.CountAsync(),
                ColisEnTransit = await _context.Colis.CountAsync(c => c.Statut == StatutColis.EnTransit),
                ColisLivres = await _context.Colis.CountAsync(c => c.Statut == StatutColis.Livre),
                TotalLivraisons = await _context.Livraisons.CountAsync(),
                LivraisonsConfirmees = await _context.Livraisons.CountAsync(l => l.Statut == StatutLivraison.Livree),
                LivraisonsEnCours = await _context.Livraisons.CountAsync(l => l.Statut == StatutLivraison.EnCours),
                TotalMoyensTransport = await _context.MoyensTransport.CountAsync(),
                TotalTrajets = await _context.Trajets.CountAsync()
            };

            ViewBag.Rapport = rapport;

            // Dernières commandes
            ViewBag.DernieresCommandes = await _context.Commandes
                .Include(c => c.Client)
                .OrderByDescending(c => c.DateCommande)
                .Take(10)
                .ToListAsync();

            // Dernières livraisons
            ViewBag.DernieresLivraisons = await _context.Livraisons
                .Include(l => l.Colis)
                .Include(l => l.Livreur)
                .OrderByDescending(l => l.Id)
                .Take(10)
                .ToListAsync();

            return View();
        }
    }
}
