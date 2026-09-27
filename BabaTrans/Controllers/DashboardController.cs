using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;
using BabaTrans.ViewModels;

namespace BabaTrans.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly UserManager<Utilisateur> _userManager;

        public DashboardController(BabaTransContext context, UserManager<Utilisateur> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(user!);
            var role = roles.FirstOrDefault() ?? "Utilisateur";
            ViewBag.UserName = $"{user!.Prenom} {user.Nom}";
            ViewBag.UserRole = role;

            var model = new DashboardViewModel();

            if (User.IsInRole("Client"))
            {
                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Email == user.Email);
                var commandes = _context.Commandes.Where(c => client != null && c.ClientId == client.Id);
                var colis = _context.Colis.Where(c => client != null && c.Commande != null && c.Commande.ClientId == client.Id);

                model.TotalCommandes = await commandes.CountAsync();
                model.CommandesEnAttente = await commandes.CountAsync(c => c.Statut == StatutCommande.EnAttente);
                model.ColisEnTransit = await colis.CountAsync(c => c.Statut == StatutColis.EnTransit);
                model.ColisLivres = await colis.CountAsync(c => c.Statut == StatutColis.Livre);
                model.TotalColis = await colis.CountAsync();
            }
            else if (User.IsInRole("Livreur"))
            {
                var livraisons = _context.Livraisons.Where(l => l.LivreurId == user.Id);
                model.TotalLivraisons = await livraisons.CountAsync();
                model.CommandesEnAttente = await livraisons.CountAsync(l => l.Statut == StatutLivraison.EnAttente);
                model.ColisEnTransit = await livraisons.CountAsync(l => l.Statut == StatutLivraison.EnCours);
                model.LivraisonsConfirmees = await livraisons.CountAsync(l => l.Statut == StatutLivraison.Livree);
                model.ColisLivres = model.LivraisonsConfirmees;
            }
            else
            {
                model.TotalClients = await _context.Clients.CountAsync();
                model.TotalCommandes = await _context.Commandes.CountAsync();
                model.TotalColis = await _context.Colis.CountAsync();
                model.TotalLivraisons = await _context.Livraisons.CountAsync();
                model.ColisEnTransit = await _context.Colis.CountAsync(c => c.Statut == StatutColis.EnTransit);
                model.LivraisonsConfirmees = await _context.Livraisons.CountAsync(l => l.Statut == StatutLivraison.Livree);
                model.CommandesEnAttente = await _context.Commandes.CountAsync(c => c.Statut == StatutCommande.EnAttente);
                model.ColisLivres = await _context.Colis.CountAsync(c => c.Statut == StatutColis.Livre);
            }

            return View(model);
        }
    }
}
