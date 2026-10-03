using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;
using BabaTrans.ViewModels;

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
            // Un comptage groupé par statut remplace une dizaine de requêtes COUNT séparées.
            var commandes = await _context.Commandes
                .GroupBy(c => c.Statut)
                .Select(g => new { Statut = g.Key, Nombre = g.Count() })
                .ToDictionaryAsync(x => x.Statut, x => x.Nombre);
            var colis = await _context.Colis
                .GroupBy(c => c.Statut)
                .Select(g => new { Statut = g.Key, Nombre = g.Count() })
                .ToDictionaryAsync(x => x.Statut, x => x.Nombre);
            var livraisons = await _context.Livraisons
                .GroupBy(l => l.Statut)
                .Select(g => new { Statut = g.Key, Nombre = g.Count() })
                .ToDictionaryAsync(x => x.Statut, x => x.Nombre);

            var model = new RapportViewModel
            {
                TotalClients = await _context.Clients.CountAsync(),
                ClientsActifs = await _context.Clients.CountAsync(c => c.EstActif),

                TotalCommandes = commandes.Values.Sum(),
                CommandesEnAttente = commandes.GetValueOrDefault(StatutCommande.EnAttente),
                CommandesEnCours = commandes.GetValueOrDefault(StatutCommande.EnCours),
                CommandesLivrees = commandes.GetValueOrDefault(StatutCommande.Livree),
                CommandesAnnulees = commandes.GetValueOrDefault(StatutCommande.Annulee),

                TotalColis = colis.Values.Sum(),
                ColisAvecTimbre = await _context.TimbresQRCode.CountAsync(),
                ColisEnTransit = colis.GetValueOrDefault(StatutColis.EnTransit) + colis.GetValueOrDefault(StatutColis.Arrive),
                ColisLivres = colis.GetValueOrDefault(StatutColis.Livre),

                TotalLivraisons = livraisons.Values.Sum(),
                LivraisonsConfirmees = livraisons.GetValueOrDefault(StatutLivraison.Livree),
                LivraisonsEnCours = livraisons.GetValueOrDefault(StatutLivraison.EnCours),
                LivraisonsEchouees = livraisons.GetValueOrDefault(StatutLivraison.Echouee),

                TotalMoyensTransport = await _context.MoyensTransport.CountAsync(),
                MoyensDisponibles = await _context.MoyensTransport.CountAsync(m => m.EstDisponible),
                DistanceTotaleKm = await _context.Commandes
                    .Where(c => c.Statut != StatutCommande.Annulee)
                    .SumAsync(c => c.DistanceKm) ?? 0m,
                LivraisonsSuiviesGps = await _context.Livraisons.CountAsync(l => l.Positions.Any()),

                DernieresCommandes = await _context.Commandes
                    .Include(c => c.Client)
                    .OrderByDescending(c => c.DateCommande)
                    .Take(10)
                    .ToListAsync(),

                DernieresLivraisons = await _context.Livraisons
                    .Include(l => l.Colis)
                    .Include(l => l.Livreur)
                    .OrderByDescending(l => l.Id)
                    .Take(10)
                    .ToListAsync()
            };

            return View(model);
        }
    }
}
