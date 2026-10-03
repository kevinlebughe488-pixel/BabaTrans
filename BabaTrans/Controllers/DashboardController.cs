using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Helpers;
using BabaTrans.Models;
using BabaTrans.Services;
using BabaTrans.ViewModels;

namespace BabaTrans.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly UserManager<Utilisateur> _userManager;
        private readonly CompteClientService _compteClient;

        public DashboardController(BabaTransContext context, UserManager<Utilisateur> userManager, CompteClientService compteClient)
        {
            _context = context;
            _userManager = userManager;
            _compteClient = compteClient;
        }

        public async Task<IActionResult> Index()
        {
            // Nom et rôle viennent du cookie de connexion : aucune requête nécessaire.
            ViewBag.UserName = User.NomComplet();
            ViewBag.UserRole = User.RolePrincipal();

            var model = new DashboardViewModel();

            if (User.IsInRole("Client"))
            {
                var client = await _compteClient.GetClientConnecteAsync(User);
                if (client == null)
                {
                    TempData["Error"] = "Aucun supermarché n'est associé à ce compte. Contactez BABA-Trans.";
                    return View(model);
                }

                var commandes = _context.Commandes.Where(c => c.ClientId == client.Id);
                var colis = _context.Colis.Where(c => c.Commande!.ClientId == client.Id);

                model.TotalCommandes = await commandes.CountAsync();
                model.CommandesEnAttente = await commandes.CountAsync(c => c.Statut == StatutCommande.EnAttente);
                model.ColisEnTransit = await colis.CountAsync(c => c.Statut == StatutColis.EnTransit || c.Statut == StatutColis.Arrive);
                model.ColisLivres = await colis.CountAsync(c => c.Statut == StatutColis.Livre);
            }
            else if (User.IsInRole("Livreur"))
            {
                var livreurId = _userManager.GetUserId(User);
                var parStatut = await _context.Livraisons
                    .Where(l => l.LivreurId == livreurId)
                    .GroupBy(l => l.Statut)
                    .Select(g => new { Statut = g.Key, Nombre = g.Count() })
                    .ToDictionaryAsync(x => x.Statut, x => x.Nombre);

                model.TotalLivraisons = parStatut.Values.Sum();
                model.LivraisonsEnAttente = parStatut.GetValueOrDefault(StatutLivraison.EnAttente);
                model.LivraisonsEnCours = parStatut.GetValueOrDefault(StatutLivraison.EnCours);
                model.LivraisonsConfirmees = parStatut.GetValueOrDefault(StatutLivraison.Livree);
            }
            else
            {
                var colisParStatut = await _context.Colis
                    .GroupBy(c => c.Statut)
                    .Select(g => new { Statut = g.Key, Nombre = g.Count() })
                    .ToDictionaryAsync(x => x.Statut, x => x.Nombre);

                model.TotalClients = await _context.Clients.CountAsync(c => c.EstActif);
                model.CommandesEnAttente = await _context.Commandes.CountAsync(c => c.Statut == StatutCommande.EnAttente);
                model.ColisEnTransit = colisParStatut.GetValueOrDefault(StatutColis.EnTransit) + colisParStatut.GetValueOrDefault(StatutColis.Arrive);
                model.ColisLivres = colisParStatut.GetValueOrDefault(StatutColis.Livre);

                // Colis prêts mais sans livraison active, dans une commande encore ouverte.
                model.ColisAAffecter = await _context.Colis.CountAsync(c =>
                    (c.Statut == StatutColis.Enregistre || c.Statut == StatutColis.QRCodeGenere)
                    && (c.Commande!.Statut == StatutCommande.EnAttente || c.Commande.Statut == StatutCommande.EnCours)
                    && !c.Livraisons.Any(l => l.Statut == StatutLivraison.EnAttente || l.Statut == StatutLivraison.EnCours));
                model.LivraisonsAttenteDepart = await _context.Livraisons.CountAsync(l => l.Statut == StatutLivraison.EnAttente);
            }

            return View(model);
        }
    }
}
