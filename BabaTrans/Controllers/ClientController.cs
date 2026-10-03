using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur,Agent")]
    public class ClientController : Controller
    {
        private readonly BabaTransContext _context;
        private readonly UserManager<Utilisateur> _userManager;

        public ClientController(BabaTransContext context, UserManager<Utilisateur> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Client
        public async Task<IActionResult> Index()
        {
            var clients = await _context.Clients
                .Include(c => c.Commandes)
                .OrderByDescending(c => c.EstActif)
                .ThenBy(c => c.NomSupermarche)
                .ToListAsync();
            return View(clients);
        }

        // GET: Client/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var client = await _context.Clients
                .Include(c => c.Commandes)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();

            ViewBag.CompteLie = await TrouverCompteClientAsync(client.Email);
            return View(client);
        }

        // GET: Client/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Client/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("NomSupermarche,Adresse,Telephone,Email,PersonneContact")] Client client)
        {
            client.Email = string.IsNullOrWhiteSpace(client.Email) ? null : client.Email.Trim();
            await ValiderEmailUniqueAsync(client.Email, idClient: null);

            if (ModelState.IsValid)
            {
                _context.Add(client);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Le supermarché « {client.NomSupermarche} » a été enregistré.";
                return RedirectToAction(nameof(Details), new { id = client.Id });
            }
            return View(client);
        }

        // GET: Client/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var client = await _context.Clients.FindAsync(id);
            if (client == null) return NotFound();
            return View(client);
        }

        // POST: Client/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,NomSupermarche,Adresse,Telephone,Email,PersonneContact,EstActif")] Client client)
        {
            if (id != client.Id) return NotFound();

            var clientExistant = await _context.Clients.FindAsync(id);
            if (clientExistant == null) return NotFound();

            client.Email = string.IsNullOrWhiteSpace(client.Email) ? null : client.Email.Trim();
            await ValiderEmailUniqueAsync(client.Email, idClient: id);

            // Changer l'email couperait le lien avec le compte Client qui se connecte avec l'ancien.
            var emailModifie = !string.Equals(clientExistant.Email, client.Email, StringComparison.OrdinalIgnoreCase);
            if (emailModifie && await TrouverCompteClientAsync(clientExistant.Email) != null)
                ModelState.AddModelError(nameof(client.Email),
                    "Un compte Client se connecte avec l'email actuel. Le modifier couperait l'accès de ce compte à ses commandes.");

            if (ModelState.IsValid)
            {
                clientExistant.NomSupermarche = client.NomSupermarche;
                clientExistant.Adresse = client.Adresse;
                clientExistant.Telephone = client.Telephone;
                clientExistant.Email = client.Email;
                clientExistant.PersonneContact = client.PersonneContact;

                // Activer ou désactiver un supermarché reste réservé à l'administrateur.
                if (User.IsInRole("Administrateur"))
                    clientExistant.EstActif = client.EstActif;

                await _context.SaveChangesAsync();
                TempData["Success"] = "Supermarché modifié avec succès.";
                return RedirectToAction(nameof(Details), new { id });
            }

            client.DateCreation = clientExistant.DateCreation;
            if (!User.IsInRole("Administrateur"))
                client.EstActif = clientExistant.EstActif;
            return View(client);
        }

        // POST: Client/Delete/5 : désactivation (les commandes sont conservées pour l'historique).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur")]
        public async Task<IActionResult> Delete(int id)
        {
            var client = await _context.Clients.FindAsync(id);
            if (client != null)
            {
                client.EstActif = false;
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Le supermarché « {client.NomSupermarche} » a été désactivé.";
            }
            return RedirectToAction(nameof(Index));
        }

        /// <summary>L'email sert à relier un compte Client à son supermarché : il doit être unique.</summary>
        private async Task ValiderEmailUniqueAsync(string? email, int? idClient)
        {
            if (string.IsNullOrEmpty(email))
                return;

            var dejaUtilise = await _context.Clients.AnyAsync(c => c.Email == email && c.Id != idClient);
            if (dejaUtilise)
                ModelState.AddModelError(nameof(Client.Email), "Cet email est déjà utilisé par un autre supermarché.");
        }

        private async Task<Utilisateur?> TrouverCompteClientAsync(string? email)
        {
            if (string.IsNullOrEmpty(email))
                return null;

            var compte = await _userManager.FindByEmailAsync(email);
            return compte != null && await _userManager.IsInRoleAsync(compte, "Client") ? compte : null;
        }
    }
}
