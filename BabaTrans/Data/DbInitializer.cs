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
        /// <summary>
        /// EnsureCreated() ne modifie jamais une base qui existe déjà.
        /// On ajoute donc ici, sans perte de données, les colonnes apparues après la création initiale.
        /// Chaque instruction est idempotente : elle ne fait rien si la colonne existe.
        /// </summary>
        public static async Task MettreAJourSchemaAsync(BabaTransContext context)
        {
            var colonnes = new (string Table, string Colonne, string Type)[]
            {
                ("Commandes", "PoidsEstimeKg", "decimal(10,2) NULL"),
                ("Commandes", "AdresseDestination", "nvarchar(250) NULL"),
                ("Commandes", "QuartierDestination", "nvarchar(150) NULL"),
                ("Commandes", "ContactDestination", "nvarchar(150) NULL"),
                ("Commandes", "TelephoneDestination", "nvarchar(50) NULL"),
                ("Commandes", "LatitudeDestination", "float NULL"),
                ("Commandes", "LongitudeDestination", "float NULL"),
                ("Commandes", "LatitudeDepart", "float NULL"),
                ("Commandes", "LongitudeDepart", "float NULL"),
                ("Commandes", "DistanceKm", "decimal(10,2) NULL"),
                ("Commandes", "DureeEstimeeMinutes", "int NULL"),
                ("Clients", "Latitude", "float NULL"),
                ("Clients", "Longitude", "float NULL"),
            };

            foreach (var (table, colonne, type) in colonnes)
            {
                var sql = "IF COL_LENGTH('" + table + "', '" + colonne + "') IS NULL "
                        + "ALTER TABLE [" + table + "] ADD [" + colonne + "] " + type + ";";
                await context.Database.ExecuteSqlRawAsync(sql);
            }

            // Positions GPS des livreurs (même structure que celle créée par EnsureCreated sur une base neuve).
            await context.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'[PositionsLivreur]', N'U') IS NULL
BEGIN
    CREATE TABLE [PositionsLivreur] (
        [Id] int NOT NULL IDENTITY,
        [Latitude] float NOT NULL,
        [Longitude] float NOT NULL,
        [PrecisionMetres] float NULL,
        [VitesseKmh] float NULL,
        [Cap] float NULL,
        [DateEnregistrement] datetime2 NOT NULL,
        [LivraisonId] int NOT NULL,
        CONSTRAINT [PK_PositionsLivreur] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PositionsLivreur_Livraisons_LivraisonId] FOREIGN KEY ([LivraisonId]) REFERENCES [Livraisons] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_PositionsLivreur_LivraisonId_DateEnregistrement] ON [PositionsLivreur] ([LivraisonId], [DateEnregistrement]);
END");

            // Le module « Trajets inter-villes » a été retiré : l'ancienne table Trajets et la colonne Commandes.TrajetId
            // ne sont plus utilisées par l'application. Elles sont laissées en place pour ne perdre aucune donnée.
        }

        /// <summary>
        /// Géolocalise les données créées avant l'ajout des cartes : supermarchés de démonstration
        /// et commandes dont la ville de destination est connue. Aucun appel réseau : distance estimée.
        /// </summary>
        private static async Task GeolocaliserDonneesExistantesAsync(BabaTransContext context, GeolocalisationService geolocalisation)
        {
            foreach (var client in await context.Clients.Where(c => c.Latitude == null).ToListAsync())
            {
                if (EmplacementsSupermarchesDemo.TryGetValue(client.NomSupermarche, out var point))
                {
                    client.Latitude = point.Latitude;
                    client.Longitude = point.Longitude;
                }
            }
            await context.SaveChangesAsync();

            var commandes = await context.Commandes
                .Include(c => c.Client)
                .Where(c => c.LatitudeDestination == null || c.DistanceKm == null)
                .ToListAsync();
            foreach (var commande in commandes)
            {
                if (commande.LatitudeDestination is null || commande.LongitudeDestination is null)
                {
                    var centreVille = geolocalisation.CoordonneesVille(commande.VilleDestination);
                    if (centreVille is null)
                        continue;
                    commande.LatitudeDestination = centreVille.Value.Latitude;
                    commande.LongitudeDestination = centreVille.Value.Longitude;
                }

                DefinirItineraireEstime(commande, commande.Client, geolocalisation);
            }
            await context.SaveChangesAsync();
        }

        private static void DefinirItineraireEstime(Commande commande, Client? client, GeolocalisationService geolocalisation)
        {
            var depart = geolocalisation.PointDeDepart(client);
            var itineraire = geolocalisation.ItineraireEstime(depart.Point,
                new PointGps(commande.LatitudeDestination!.Value, commande.LongitudeDestination!.Value));
            commande.LatitudeDepart = depart.Point.Latitude;
            commande.LongitudeDepart = depart.Point.Longitude;
            commande.DistanceKm = itineraire.DistanceKm;
            commande.DureeEstimeeMinutes = itineraire.DureeMinutes;
        }

        private static readonly Dictionary<string, PointGps> EmplacementsSupermarchesDemo = new()
        {
            ["Kin Marché - Gombe"] = new PointGps(-4.3050, 15.3070),
            ["SK Hypermarket - Lubumbashi"] = new PointGps(-11.6600, 27.4790),
            ["City Market - Matadi"] = new PointGps(-5.8200, 13.4560),
        };

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<Utilisateur>>();
            var context = serviceProvider.GetRequiredService<BabaTransContext>();
            var qrService = serviceProvider.GetRequiredService<QRCodeService>();
            var geolocalisation = serviceProvider.GetRequiredService<GeolocalisationService>();

            await MettreAJourSchemaAsync(context);

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
            var adminUser = await userManager.FindByEmailAsync(adminEmail) ?? await userManager.FindByNameAsync(adminEmail);
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
            else
            {
                adminUser.UserName = adminEmail;
                adminUser.Email = adminEmail;
                adminUser.Nom = "Kalonji";
                adminUser.Prenom = "Alain";
                adminUser.EmailConfirmed = true;
                adminUser.EstActif = true;
                await userManager.UpdateAsync(adminUser);

                if (!await userManager.IsInRoleAsync(adminUser, "Administrateur"))
                {
                    await userManager.AddToRoleAsync(adminUser, "Administrateur");
                }
            }

            // 3. Agent par défaut
            var agentEmail = "agent@babatrans.cd";
            var agentUser = await userManager.FindByEmailAsync(agentEmail) ?? await userManager.FindByNameAsync(agentEmail);
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
            else
            {
                agentUser.UserName = agentEmail;
                agentUser.Email = agentEmail;
                agentUser.Nom = "Mukendi";
                agentUser.Prenom = "Jean";
                agentUser.Matricule = "AGT-2026-001";
                agentUser.EmailConfirmed = true;
                agentUser.EstActif = true;
                await userManager.UpdateAsync(agentUser);

                if (!await userManager.IsInRoleAsync(agentUser, "Agent"))
                {
                    await userManager.AddToRoleAsync(agentUser, "Agent");
                }
            }

            // 4. Livreur par défaut
            var livreurEmail = "livreur@babatrans.cd";
            var livreurUser = await userManager.FindByEmailAsync(livreurEmail) ?? await userManager.FindByNameAsync(livreurEmail);
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
            else
            {
                livreurUser.UserName = livreurEmail;
                livreurUser.Email = livreurEmail;
                livreurUser.Nom = "Kabongo";
                livreurUser.Prenom = "Pierre";
                livreurUser.Immatriculation = "LIV-KIN-101";
                livreurUser.EmailConfirmed = true;
                livreurUser.EstActif = true;
                await userManager.UpdateAsync(livreurUser);

                if (!await userManager.IsInRoleAsync(livreurUser, "Livreur"))
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
            else if (!await userManager.IsInRoleAsync(clientUser, "Client"))
            {
                await userManager.AddToRoleAsync(clientUser, "Client");
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
                    PersonneContact = "Marc Ilunga (Directeur Achat)",
                    Latitude = EmplacementsSupermarchesDemo["Kin Marché - Gombe"].Latitude,
                    Longitude = EmplacementsSupermarchesDemo["Kin Marché - Gombe"].Longitude
                };
                var cl2 = new Client
                {
                    NomSupermarche = "SK Hypermarket - Lubumbashi",
                    Adresse = "Chaussée M'siri n°450, Lubumbashi",
                    Telephone = "+243 99 777 0202",
                    Email = "logistique@sk-lubum.cd",
                    PersonneContact = "Sarah Kabasele",
                    Latitude = EmplacementsSupermarchesDemo["SK Hypermarket - Lubumbashi"].Latitude,
                    Longitude = EmplacementsSupermarchesDemo["SK Hypermarket - Lubumbashi"].Longitude
                };
                var cl3 = new Client
                {
                    NomSupermarche = "City Market - Matadi",
                    Adresse = "Boulevard du Port, Ville Basse, Matadi",
                    Telephone = "+243 82 333 0303",
                    Email = "contact@citymarket-matadi.cd",
                    PersonneContact = "David Mbaya",
                    Latitude = EmplacementsSupermarchesDemo["City Market - Matadi"].Latitude,
                    Longitude = EmplacementsSupermarchesDemo["City Market - Matadi"].Longitude
                };

                await context.Clients.AddRangeAsync(cl1, cl2, cl3);
                await context.SaveChangesAsync();

                // 7. Véhicule de la livraison de démonstration
                var camion = await context.MoyensTransport.FirstOrDefaultAsync(m => m.Type == "Camion");

                // 8. Commande de démonstration
                var cmd1 = new Commande
                {
                    ClientId = cl1.Id,
                    VilleDestination = "Matadi",
                    LatitudeDestination = EmplacementsSupermarchesDemo["City Market - Matadi"].Latitude,
                    LongitudeDestination = EmplacementsSupermarchesDemo["City Market - Matadi"].Longitude,
                    PoidsEstimeKg = 150m,
                    AdresseDestination = "Boulevard du Port, Ville Basse",
                    ContactDestination = "David Mbaya (City Market)",
                    TelephoneDestination = "+243 82 333 0303",
                    Description = "Palette de produits secs, boissons gazeuses et huiles de table",
                    DateCommande = DateTime.Now.AddDays(-2),
                    Statut = StatutCommande.EnCours
                };
                DefinirItineraireEstime(cmd1, cl1, geolocalisation);
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
                var qrContenu = qrService.GenererContenuColis(colis1.Id, codeSuivi, colis1.Description, colis1.DateEnregistrement);
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

                // 12. Dernières positions GPS remontées par le téléphone du livreur sur la RN1
                var etapes = new[] { (-4.3980, 15.2600), (-4.5900, 15.1700), (-5.1300, 15.0700), (-5.2500, 14.8650) };
                for (var i = 0; i < etapes.Length; i++)
                {
                    livraison.Positions.Add(new PositionLivreur
                    {
                        Latitude = etapes[i].Item1,
                        Longitude = etapes[i].Item2,
                        PrecisionMetres = 15,
                        VitesseKmh = 45,
                        DateEnregistrement = livraison.DateDepart.Value.AddHours(i + 1)
                    });
                }

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

            await GeolocaliserDonneesExistantesAsync(context, geolocalisation);
        }
    }
}
