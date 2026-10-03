# 🚚 CONTEXTE COMPLET DU PROJET BABA-TRANS (GUIDE DE TRANSMISSION)

> **Document de référence à destination des développeurs et agents IA.**  
> Ce document synthétise l'intégralité du projet : expression des besoins, architecture logicielle, modèle de données, sécurité RBAC, implémentation des diagrammes UML, design system et procédures opérationnelles. Il évite d'avoir à relire le code source fichier par fichier.

---

## 1. Contexte Métier & Énoncé

### 1.1 Présentation de l'entreprise
**BABA-Trans** est une entreprise de logistique et de transport en République Démocratique du Congo (RDC), spécialisée dans l'acheminement de marchandises pour le compte de **supermarchés partenaires** (ex: Kin Marché, SK Hypermarket, City Market, etc.) à travers les grandes villes du pays (Kinshasa, Matadi, Lubumbashi, Goma, etc.).

### 1.2 Problématique initiale
- L'ancien système interne était devenu obsolète et non extensible.
- Nécessité de moderniser l'infrastructure et d'augmenter la productivité.
- **Besoin clé :** mise en place d'un système de traçabilité numérique par **timbres QR-code** imprimables sur chaque colis pour sécuriser la chaîne d'expédition.

### 1.3 Stack Technique Imposée
- **Framework :** ASP.NET Core MVC (.NET 9)
- **ORM :** Entity Framework Core (Code First)
- **Base de données :** Microsoft SQL Server (`MSSQLSERVER` sur `DESKTOP-95S6R2F`)
- **Authentification & Autorisations :** ASP.NET Core Identity avec RBAC (Role-Based Access Control)
- **Génération QR :** Bibliothèque NuGet `QRCoder`
- **Frontend :** Bootstrap 5 + Vanilla CSS moderne (Thème blanc et sidebar bleu ciel)

---

## 2. Respect & Implémentation des 3 Diagrammes UML

Le système est la traduction logicielle exacte des trois diagrammes fournis :

```
       [ Supermarché ]                      [ Agent BABA-Trans ]
              │                                      │
              ▼                                      ▼
       1. Passe Commande                     2. Enregistre Colis
              │                                      │
              └──────────────► Système ◄─────────────┘
                                  │
                   Génère Timbre QR-Code (Base64)
                                  │
                                  ▼
                   Affecte Colis, Livreur & Véhicule (Agent)
                                  │
                                  ▼
                             [ Livreur ]
                                  │
                        3. Scan Départ (En Transit)
                        4. Acheminement suivi par GPS (temps réel)
                        5. Scan Arrivée (Arrivé)
                        6. Confirmer Livraison (Livré)
                                  │
                                  ▼
                          [ Administrateur ]
                       Supervise & Consulte Rapports
```

### 2.1 Diagramme de Cas d'Utilisation (Use Case) & Matrice RBAC
Le système distingue **4 acteurs métiers** avec des privilèges stricts :

| Cas d'Utilisation | Administrateur | Agent BABA-Trans | Livreur | Client (Supermarché) | Contrôleur & Action |
|---|:---:|:---:|:---:|:---:|---|
| **S'authentifier / Déconnexion** | ✅ | ✅ | ✅ | ✅ | `AccountController` (`Login`, `Logout`) |
| **Gérer les utilisateurs (RBAC)** | ✅ | ❌ | ❌ | ❌ | `AccountController` (`Users`, `Register`, `ToggleUser`) |
| **Gérer les Supermarchés (Clients)** | ✅ | ✅ | ❌ | ❌ | `ClientController` (CRUD complet) |
| **Passer / Déposer une commande** | ✅ | ✅ | ❌ | ✅ | `CommandeController` (`Create`, `Index`) |
| **Enregistrer un colis** | ✅ | ✅ | ❌ | ❌ | `ColisController` (`Create`) |
| **Générer le timbre QR-code** | ✅ | ✅ (Automatique) | ❌ | ❌ | `ColisController` (`GenererQRCode`) via `QRCodeService` |
| **Affecter moyen de transport / tournée** | ✅ | ✅ | ❌ | ❌ | `LivraisonController` (`Create`), `TransportController` (`Moyens`) |
| **Estimer le tarif (poids + distance)** | ✅ | ✅ | ❌ | ✅ | `CommandeController` (`EstimerTarif`) via `TarificationService` et `GeolocalisationService` |
| **Partager sa position GPS** | ❌ | ❌ | ✅ (livreur affecté) | ❌ | `SuiviGpsController` (`EnvoyerPosition`) |
| **Suivre le livreur en temps réel** | ✅ | ✅ | ✅ (ses livraisons) | ✅ (ses colis) | `SuiviGpsController` (`Position`), `Carte` et `Flotte` pour le personnel |
| **Suivre un colis** | ✅ | ✅ | ✅ | ✅ (Public) | `ColisController` (`Suivi`, `Details`) |
| **Scanner le colis (Départ & Arrivée)** | ✅ | ✅ | ✅ | ❌ | `LivraisonController` (`ScannerDepart`, `ScannerArrivee`) |
| **Confirmer la livraison** | ✅ | ✅ | ✅ | ❌ | `LivraisonController` (`Confirmer`) |
| **Consulter les rapports & indicateurs** | ✅ | ❌ | ❌ | ❌ | `RapportController` (`Index`) |

### 2.2 Diagramme de Classes & Entités EF Core

Le modèle comprend 8 entités interconnectées dans [`Data/BabaTransContext.cs`](file:///c:/Users/kevin/Desktop/atelier%20génie%20logiciel/BabaTrans/Data/BabaTransContext.cs) :

1. **`Utilisateur`** (hérite de `IdentityUser`) :
   - Champs : `Nom`, `Prenom`, `DateCreation`, `EstActif`, `Matricule` (pour Agent), `Immatriculation` (pour Livreur).
2. **`Client`** :
   - Représente le supermarché partenaire (`NomSupermarche`, `Adresse`, `Telephone`, `Email`, `PersonneContact`, `EstActif`).
   - Emplacement facultatif (`Latitude`, `Longitude`) : point de départ des livraisons pour le calcul de la distance (sinon, dépôt BABA-Trans).
   - Relation : `1 Client` $\rightarrow$ `0..* Commandes`.
3. **`Commande`** :
   - Représente le bon d'expédition (`DateCommande`, `VilleDestination`, `Description`, `Statut`, `PoidsEstimeKg`, adresse et contact du destinataire).
   - Géolocalisation : `LatitudeDestination`/`LongitudeDestination` (point choisi sur la carte), puis, calculés par le serveur, `LatitudeDepart`/`LongitudeDepart`, `DistanceKm` et `DureeEstimeeMinutes`.
   - Clé étrangère : `ClientId`.
   - Relation : `1 Commande` $\rightarrow$ `1..* Colis`.
4. **`Colis`** :
   - Représente un paquet physique (`Description`, `Poids`, `Statut`, `DateEnregistrement`, `CodeSuivi`).
   - Clé étrangère : `CommandeId`.
   - Relation : `1 Colis` $\rightarrow$ `1 TimbreQRCode` (1-à-1) et `0..* Livraisons`.
5. **`TimbreQRCode`** :
   - Stocke l'empreinte QR (`Contenu`, `ImageBase64`, `DateGeneration`, `ColisId`).
   - Format contenu : `BABATRANS|COLIS:{id}|CODE:{code}|DESC:{desc}|DATE:{timestamp}`.
6. **`Livraison`** :
   - Représente une expédition/tournée (`DateDepart`, `DateArrivee`, `DateLivraison`, `Statut`, `Commentaire`).
   - Clés étrangères : `ColisId`, `LivreurId` (vers `Utilisateur`), `MoyenTransportId` (vers `MoyenTransport`).
   - **Lien direct avec le véhicule** : l'Agent affecte le moyen de transport au moment de la création de la livraison, assurant la traçabilité complète du flux logistique.
7. **`PositionLivreur`** :
   - Position GPS envoyée par le téléphone du livreur pendant une livraison en cours (`Latitude`, `Longitude`, `PrecisionMetres`, `VitesseKmh`, `Cap`, `DateEnregistrement`).
   - Clé étrangère : `LivraisonId` (suppression en cascade). L'historique forme la trace du trajet parcouru.
8. **`MoyenTransport`** :
   - Véhicule de la flotte (`Type`, `Immatriculation`, `Capacite`, `EstDisponible`, `Description`).

### 2.3 Diagramme de Séquence (Cycle de Vie Opérationnel)
Le flux suit rigoureusement les étapes de l'énoncé :
1. **Dépôt commande :** Le client supermarché ou l'agent initie une `Commande`.
2. **Enregistrement colis :** L'agent saisit le `Colis` $\rightarrow$ Le système génère automatiquement un code de suivi unique (`BT-YYYYMMDD-XXXXXXXX`) et calcule son image QR en PNG Base64 (`TimbreQRCode`). Le timbre est imprimable immédiatement.
3. **Affectation :** L'Agent (ou l'Administrateur) crée la `Livraison` en affectant le `Colis` au `Livreur` responsable et au `MoyenTransport`. Le livreur ne choisit pas le moyen de transport ni son affectation, il reçoit la mission pré-affectée.
4. **Scan départ :** Le livreur clique sur "Valider Scan Départ" $\rightarrow$ Le colis passe en statut `EnTransit` et la livraison en `EnCours`.
5. **Acheminement :** Le livreur active le partage GPS sur son téléphone ; sa position est envoyée toutes les 10 secondes (`PositionLivreur`). Le personnel et le supermarché expéditeur suivent le livreur en temps réel sur une carte (distance restante, heure d'arrivée estimée).
6. **Scan arrivée :** Le livreur valide le "Scan Arrivée" $\rightarrow$ Le colis passe au statut `Arrive`.
7. **Confirmation livraison :** Le livreur saisit le nom du signataire ou un commentaire $\rightarrow$ Le colis passe en `Livre`, la livraison en `Livree`. Si tous les colis d'une commande sont livrés, la commande globale est automatiquement clôturée en `Livree`.

### 2.4 Tarification & Géolocalisation
- **Choix de la destination :** le client place le point de livraison sur une carte Leaflet / OpenStreetMap (clic, repère déplaçable, recherche d'adresse Nominatim ou « Ma position »). Ville, quartier et adresse sont pré-remplis.
- **Distance :** calculée par le serveur (jamais confiance au navigateur) depuis le supermarché (ou le dépôt) jusqu'au point de livraison, par la route via un service OSRM. En cas d'indisponibilité, distance à vol d'oiseau × coefficient routier (1,3). Les livraisons sont limitées à la RDC.
- **Tarif :** `forfait + poids × tarif/kg + distance × tarif/km`, avec un minimum facturable, arrondi au millier de FC supérieur (`Tarification` dans `appsettings.json`).
- **Réglages :** section `Geolocalisation` de `appsettings.json` (dépôt, coefficient routier, vitesse moyenne, services d'itinéraire et de géocodage, tuiles de carte, intervalle de rafraîchissement, villes connues).
- **HTTPS :** les navigateurs n'autorisent le GPS que sur une page sécurisée (`https://`) ou sur `localhost`. Pour un livreur sur téléphone, lancer l'application en HTTPS (profil `https`).
- **Ancien module « Trajets inter-villes » :** retiré. Sur une base existante, la table `Trajets` et la colonne `Commandes.TrajetId` sont conservées mais ne sont plus utilisées.

---

## 3. Configuration Base de Données & Environnement

### 3.1 Serveur SQL Server
- **Nom de la machine / Instance :** `DESKTOP-95S6R2F`
- **Nom de la base de données :** `BabaTransDB`
- **Chaîne de connexion** (dans [`appsettings.json`](file:///c:/Users/kevin/Desktop/atelier%20génie%20logiciel/BabaTrans/appsettings.json)) :
  ```json
  "Server=DESKTOP-95S6R2F;Database=BabaTransDB;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  ```
- **Initialisation automatique :** À chaque démarrage dans [`Program.cs`](file:///c:/Users/kevin/Desktop/atelier%20génie%20logiciel/BabaTrans/Program.cs), `context.Database.EnsureCreated()` crée la base et les tables si elles n'existent pas, puis `DbInitializer.SeedAsync()` injecte les rôles et le jeu d'essai.

### 3.2 Comptes Démo Préconfigurés (avec mot de passe standardisé)

| Rôle | Email | Mot de passe | Rôle RBAC | Spécificités |
|---|---|---|---|---|
| **Admin** | `admin@babatrans.cd` | `Admin@123` | `Administrateur` | Accès absolu, gestion des comptes et rapports |
| **Agent** | `agent@babatrans.cd` | `Agent@123` | `Agent` | Matricule `AGT-2026-001`, gestion colis & QR |
| **Livreur** | `livreur@babatrans.cd` | `Livreur@123` | `Livreur` | Immatriculation `LIV-KIN-101`, scans départ/arrivée |
| **Client** | `client@kinmarche.cd` | `Client@123` | `Client` | Directeur achat chez Kin Marché |

> **Astuce UI :** Sur la page de connexion ([http://localhost:5000/Account/Login](http://localhost:5000/Account/Login)), 4 boutons "1-Click Fill" pré-remplissent automatiquement ces identifiants pour tester chaque rôle instantanément sans rien taper.

---

## 4. Design System & Charte Graphique

L'application utilise un design **Blanc & Bleu Ciel** avec Bootstrap 5 :

- **Couleur principale (Sidebar) :** Bleu Ciel solide uniforme (`#0d6efd`) sans dégradé.
- **Sidebar :** Typographie blanche, onglet actif blanc sur fond blanc avec texte bleu ciel (`#0d6efd`), footer avec badge de rôle.
- **Arrière-plan principal :** Blanc épuré (`#f8fafc`).
- **Cartes & Conteneurs (`.bt-card`) :** Fond blanc pur (`#ffffff`), bordures discrètes (`#e2e8f0`), ombres douces.
- **Typographie :** Police Google Fonts *Inter*, titres en ardoise sombre (`#0f172a`), corps de texte en gris ardoise (`#334155`).
- **Statuts & Badges :**
  - Vert Émeraude (`#059669`) : Livré, Confirmé, Actif.
  - Orange Logistique (`#ea580c`) : En Transit.
  - Jaune Ambre (`#d97706`) : En Attente.
  - Bleu Ciel Solide (`#0d6efd`) : QR-Code généré, Information, Boutons principaux.
  - Rouge / Rose (`#dc2626`) : Inactif, Annulé.

---

## 5. Architecture des Dossiers & Rôles des Fichiers

```
BabaTrans/
├── Controllers/
│   ├── AccountController.cs       # Login, Logout, Register (RBAC), Users management, ToggleUser
│   ├── DashboardController.cs     # Statistiques globales, KPI et timeline
│   ├── ClientController.cs        # CRUD des supermarchés partenaires (Admin & Agent)
│   ├── CommandeController.cs      # CRUD des commandes expéditeur (Admin, Agent, Client)
│   ├── ColisController.cs         # Enregistrement colis, QR generation, suivi public (/Colis/Suivi)
│   ├── LivraisonController.cs     # Affectation tournée, ScannerDepart, ScannerArrivee, Confirmer
│   ├── TransportController.cs     # Flotte (MoyensTransport)
│   ├── SuiviGpsController.cs      # Positions GPS du livreur, suivi en temps réel, carte de la flotte
│   └── RapportController.cs       # Statistiques avancées et indicateurs SI (Admin)
├── Models/
│   ├── Utilisateur.cs             # Extension de IdentityUser (Matricule, Immatriculation)
│   ├── Client.cs                  # Entité Supermarché
│   ├── Commande.cs                # Entité Commande + Enum StatutCommande
│   ├── Colis.cs                   # Entité Colis + Enum StatutColis
│   ├── TimbreQRCode.cs            # Entité QR Code (Image Base64)
│   ├── Livraison.cs               # Entité Livraison + Enum StatutLivraison
│   ├── PositionLivreur.cs         # Entité Position GPS du livreur
│   └── MoyenTransport.cs          # Entité Véhicule (Camion, Van, Moto...)
├── Data/
│   ├── BabaTransContext.cs        # DbContext EF Core avec Fluent API & seed matériel
│   └── DbInitializer.cs           # Seeder des 4 rôles, comptes démo, clients, colis test, mise à jour du schéma
├── Services/
│   ├── QRCodeService.cs           # Service wrapper QRCoder générant PNG en Base64
│   ├── TarificationService.cs     # Tarif = forfait + poids + distance
│   └── GeolocalisationService.cs  # Distance, itinéraire routier (OSRM), zone desservie
├── ViewModels/
│   └── ViewModels.cs              # LoginViewModel, RegisterViewModel, DashboardViewModel, SuiviColisViewModel
├── Views/
│   ├── Shared/
│   │   ├── _Layout.cshtml         # Layout Bootstrap 5, topbar, alertes TempData, scripts
│   │   ├── _Sidebar.cshtml        # Menu latéral dynamique filtré par rôle RBAC (Bleu ciel)
│   │   ├── _SuiviGps.cshtml       # Carte de suivi en direct + partage de position du livreur
│   │   ├── _CarteScripts.cshtml   # Leaflet + réglages des cartes + carte.js
│   │   └── _ValidationScriptsPartial.cshtml
│   ├── Account/                   # Login.cshtml, Register.cshtml, Users.cshtml, AccessDenied.cshtml
│   ├── Dashboard/                 # Index.cshtml (KPI cards, stepper, actions rapides)
│   ├── Client/                    # Index.cshtml, Create.cshtml, Edit.cshtml, Details.cshtml
│   ├── Commande/                  # Index.cshtml, Create.cshtml, Edit.cshtml, Details.cshtml
│   ├── Colis/                     # Index.cshtml, Create.cshtml, Details.cshtml (Timbre), Suivi.cshtml
│   ├── Livraison/                 # Index.cshtml, Create.cshtml, Details.cshtml (Scans & Confirmation)
│   ├── Transport/                 # Moyens.cshtml, CreateMoyen.cshtml, EditMoyen.cshtml
│   ├── SuiviGps/                  # Carte.cshtml (livreurs en route, en direct)
│   └── Rapport/                   # Index.cshtml (Rapports complets imprimables)
├── wwwroot/
│   ├── css/site.css               # Feuille de style complète Blanc & Bleu Ciel
│   ├── js/carte.js                # Cartes, choix du point de livraison, estimation, suivi et partage GPS
│   ├── lib/leaflet/               # Leaflet 1.9.4 (cartes OpenStreetMap)
│   └── lib/bootstrap/             # Bootstrap 5 assets locaux
├── appsettings.json               # Chaîne de connexion SQL Server
└── Program.cs                     # Injection de dépendances, middleware, auth cookie, startup
```

---

## 6. Guide Opérationnel pour le Prochain Agent

### 6.1 Compiler le projet
Dans le terminal PowerShell (dans `c:\Users\kevin\Desktop\atelier génie logiciel\BabaTrans`) :
```powershell
dotnet build
```

### 6.2 Lancer l'application
```powershell
dotnet run --urls "http://localhost:5000"
```
L'application démarre immédiatement sur le port 5000. Le premier lancement initialise automatiquement `BabaTransDB` sur `DESKTOP-95S6R2F`.

### 6.3 Pour apporter des modifications
- **Ajouter un nouveau champ à une entité :**
  1. Modifier la classe dans `Models/`.
  2. Mettre à jour `Data/BabaTransContext.cs` si nécessaire.
  3. Mettre à jour les vues (`Create`, `Edit`, `Details`) associées.
- **Ajouter une nouvelle règle d'autorisation RBAC :**
  Utiliser l'attribut `[Authorize(Roles = "NomDuRole")]` au niveau du contrôleur ou de l'action.
- **Modifier le style :**
  Tous les tokens de couleur et règles graphiques sont centralisés dans [`BabaTrans/wwwroot/css/site.css`](file:///c:/Users/kevin/Desktop/atelier%20génie%20logiciel/BabaTrans/wwwroot/css/site.css). Ne pas utiliser de classes Tailwind ad-hoc car le projet repose sur Bootstrap 5 et ce CSS.
