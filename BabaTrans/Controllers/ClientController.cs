using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur,Agent")]
    public class ClientController : Controller
    {
        private readonly BabaTransContext _context;

        public ClientController(BabaTransContext context)
        {
            _context = context;
        }

        private bool EstGestionnaireClient()
        {
            return User.IsInRole("Administrateur") || User.IsInRole("Agent");
        }

        // GET: Client
        public async Task<IActionResult> Index()
        {
            if (!EstGestionnaireClient())
                return Forbid();

            var clients = await _context.Clients
                .Include(c => c.Commandes)
                .OrderByDescending(c => c.DateCreation)
                .ToListAsync();
            return View(clients);
        }

        // GET: Client/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (!EstGestionnaireClient())
                return Forbid();

            if (id == null) return NotFound();
            var client = await _context.Clients
                .Include(c => c.Commandes)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();
            return View(client);
        }

        // GET: Client/Create
        public IActionResult Create()
        {
            if (!EstGestionnaireClient())
                return Forbid();

            return View();
        }

        // POST: Client/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("NomSupermarche,Adresse,Telephone,Email,PersonneContact")] Client client)
        {
            if (!EstGestionnaireClient())
                return Forbid();

            if (ModelState.IsValid)
            {
                _context.Add(client);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Client créé avec succès.";
                return RedirectToAction(nameof(Index));
            }
            return View(client);
        }

        // GET: Client/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!EstGestionnaireClient())
                return Forbid();

            if (id == null) return NotFound();
            var client = await _context.Clients.FindAsync(id);
            if (client == null) return NotFound();
            return View(client);
        }

        // POST: Client/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,NomSupermarche,Adresse,Telephone,Email,PersonneContact")] Client client)
        {
            if (!EstGestionnaireClient())
                return Forbid();

            if (id != client.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var clientExistant = await _context.Clients.FindAsync(id);
                    if (clientExistant == null) return NotFound();

                    clientExistant.NomSupermarche = client.NomSupermarche;
                    clientExistant.Adresse = client.Adresse;
                    clientExistant.Telephone = client.Telephone;
                    clientExistant.Email = client.Email;
                    clientExistant.PersonneContact = client.PersonneContact;
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Client modifié avec succès.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Clients.Any(c => c.Id == id)) return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(client);
        }

        // POST: Client/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrateur")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!EstGestionnaireClient())
                return Forbid();

            var client = await _context.Clients.FindAsync(id);
            if (client != null)
            {
                client.EstActif = false;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Client désactivé avec succès.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
