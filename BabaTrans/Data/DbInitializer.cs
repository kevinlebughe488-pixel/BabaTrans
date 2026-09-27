using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Models;
using BabaTrans.Services;

namespace BabaTrans.Data
{
    /// <summary>
    /// Initialise la base de données avec les rôles RBAC, les utilisateurs par défaut et un jeu de données initiales.
    /// </summary>
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<Utilisateur>>();
            var context = serviceProvider.GetRequiredService<BabaTransContext>();
            var qrService = serviceProvider.GetRequiredService<QRCodeService>();

            // 1. Créer les 4 rôles RBAC
            string[] roles = { "Administrateur", "Agent", "Livreur", "Client" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // 2. Administrateur par défaut
            var adminEmail = "admin@babatrans.cd";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new Utilisateur
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    Nom = "Kalonji",
                    Prenom = "Alain",
                    EmailConfirmed = true,
                    EstActif = true
                };
                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Administrateur");
                }
            }

            // 3. Agent par défaut
            var agentEmail = "agent@babatrans.cd";
            var agentUser = await userManager.FindByEmailAsync(agentEmail);
            if (agentUser == null)
            {
                agentUser = new Utilisateur
                {
                    UserName = agentEmail,
                    Email = agentEmail,
                    Nom = "Mukendi",
                    Prenom = "Jean",
                    EmailConfirmed = true,
                    EstActif = true,
                    Matricule = "AGT-2026-001"
                };
                var result = await userManager.CreateAsync(agentUser, "Agent@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(agentUser, "Agent");
                }
            }

            // 4. Livreur par défaut
            var livreurEmail = "livreur@babatrans.cd";
            var livreurUser = await userManager.FindByEmailAsync(livreurEmail);
            if (livreurUser == null)
            {
                livreurUser = new Utilisateur
                {
                    UserName = livreurEmail,
                    Email = livreurEmail,
                    Nom = "Kabongo",
                    Prenom = "Pierre",
                    EmailConfirmed = true,
                    EstActif = true,
                    Immatriculation = "LIV-KIN-101"
                };
                var result = await userManager.CreateAsync(livreurUser, "Livreur@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(livreurUser, "Livreur");
                }
            }

            // 5. Client par défaut
            var clientUserEmail = "client@kinmarche.cd";
            var clientUser = await userManager.FindByEmailAsync(clientUserEmail);
            if (clientUser == null)
            {
                clientUser = new Utilisateur
                {
                    UserName = clientUserEmail,
                    Email = clientUserEmail,
                    Nom = "Ilunga",
                    Prenom = "Marc",
                    EmailConfirmed = true,
                    EstActif = true
                };
                var result = await userManager.CreateAsync(clientUser, "Client@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(clientUser, "Client");
                }
            }

            // 6. Supermarchés (Clients)
            if (!await context.Clients.AnyAsync())
            {
                var cl1 = new Client
                {
                    NomSupermarche = "Kin Marché - Gombe",
                    Adresse = "Avenue de la Nation n°12, Gombe, Kinshasa",
                    Telephone = "+243 81 555 0101",
                    Email = "client@kinmarche.cd",
                    PersonneContact = "Marc Ilunga (Directeur Achat)"
                };
                var cl2 = new Client
                {
                    NomSupermarche = "SK Hypermarket - Lubumbashi",
                    Adresse = "Chaussée M'siri n°450, Lubumbashi",
                    Telephone = "+243 99 777 0202",
                    Email = "logistique@sk-lubum.cd",
                    PersonneContact = "Sarah Kabasele"
                };
                var cl3 = new Client
                {
                    NomSupermarche = "City Market - Matadi",
                    Adresse = "Boulevard du Port, Ville Basse, Matadi",
                    Telephone = "+243 82 333 0303",
                    Email = "contact@citymarket-matadi.cd",
                    PersonneContact = "David Mbaya"
                };

                await context.Clients.AddRangeAsync(cl1, cl2, cl3);
                await context.SaveChangesAsync();

                // 7. Trajets inter-villes
                var camion = await context.MoyensTransport.FirstOrDefaultAsync(m => m.Type == "Camion");
                var van = await context.MoyensTransport.FirstOrDefaultAsync(m => m.Type == "Camionnette");

                var t1 = new Trajet
                {
                    VilleDepart = "Kinshasa",
                    VilleArrivee = "Matadi",
                    DistanceKm = 352,
                    DureeEstimeeHeures = 6.5,
                    MoyenTransportId = camion?.Id
                };
                var t2 = new Trajet
                {
                    VilleDepart = "Kinshasa",
                    VilleArrivee = "Lubumbashi",
                    DistanceKm = 2280,
                    DureeEstimeeHeures = 48.0,
                    MoyenTransportId = camion?.Id
                };
                var t3 = new Trajet
                {
                    VilleDepart = "Kinshasa",
                    VilleArrivee = "Goma",
                    DistanceKm = 1950,
                    DureeEstimeeHeures = 36.0,
                    MoyenTransportId = van?.Id
                };

                await context.Trajets.AddRangeAsync(t1, t2, t3);
                await context.SaveChangesAsync();

                // 8. Commande de démonstration
                var cmd1 = new Commande
                {
                    ClientId = cl1.Id,
                    TrajetId = t1.Id,
                    VilleDestination = "Matadi",
                    Description = "Palette de produits secs, boissons gazeuses et huiles de table",
                    DateCommande = DateTime.Now.AddDays(-2),
                    Statut = StatutCommande.EnCours
                };
                await context.Commandes.AddAsync(cmd1);
                await context.SaveChangesAsync();

                // 9. Colis de démonstration
                var codeSuivi = $"BT-{DateTime.Now:yyyyMMdd}-L09G2601";
                var colis1 = new Colis
                {
                    CommandeId = cmd1.Id,
                    Description = "Cartons d'huiles raffinées et conserves alimentaires",
                    Poids = 145.5,
                    DateEnregistrement = DateTime.Now.AddDays(-2),
                    CodeSuivi = codeSuivi,
                    Statut = StatutColis.EnTransit
                };
                await context.Colis.AddAsync(colis1);
                await context.SaveChangesAsync();

                // 10. Timbre QR-Code généré pour ce colis
                var qrContenu = qrService.GenererContenuColis(colis1.Id, codeSuivi, colis1.Description);
                var qrBase64 = qrService.GenererQRCode(qrContenu);

                var timbre = new TimbreQRCode
                {
                    ColisId = colis1.Id,
                    Contenu = qrContenu,
                    ImageBase64 = qrBase64,
                    DateGeneration = DateTime.Now.AddDays(-2)
                };
                await context.TimbresQRCode.AddAsync(timbre);

                // 11. Expédition / Livraison avec scans
                var livraison = new Livraison
                {
                    ColisId = colis1.Id,
                    LivreurId = livreurUser?.Id,
                    MoyenTransportId = camion?.Id,
                    DateDepart = DateTime.Now.AddDays(-1),
                    Statut = StatutLivraison.EnCours,
                    Commentaire = "Départ de Kinshasa vers le supermarché de Matadi"
                };
                await context.Livraisons.AddAsync(livraison);

                await context.SaveChangesAsync();
            }

            var clientKinMarche = await context.Clients
                .FirstOrDefaultAsync(c => c.Email == clientUserEmail);
            if (clientKinMarche == null)
            {
                clientKinMarche = await context.Clients
                    .FirstOrDefaultAsync(c => c.NomSupermarche == "Kin Marché - Gombe");

                if (clientKinMarche == null)
                {
                    clientKinMarche = new Client
                    {
                        NomSupermarche = "Kin Marché - Gombe",
                        Adresse = "Avenue de la Nation n°12, Gombe, Kinshasa",
                        Telephone = "+243 81 555 0101",
                        Email = clientUserEmail,
                        PersonneContact = "Marc Ilunga (Directeur Achat)"
                    };
                    await context.Clients.AddAsync(clientKinMarche);
                }
                else
                {
                    clientKinMarche.Email = clientUserEmail;
                }

                await context.SaveChangesAsync();
            }
        }
    }
}
