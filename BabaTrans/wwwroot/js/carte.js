// BABA-Trans : cartes (Leaflet + OpenStreetMap), choix du point de livraison, estimation du tarif
// et suivi GPS des livreurs en temps réel.
// Chaque bloc de page porte un attribut data-bt-carte et il est initialisé automatiquement en bas de ce fichier.

(function () {
    'use strict';

    const config = Object.assign({
        tuiles: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
        attribution: '&copy; OpenStreetMap',
        geocodage: 'https://nominatim.openstreetmap.org',
        centre: [-4.3219, 15.3222],
        zoom: 11,
        vueRdc: { centre: [-3.5, 23.5], zoom: 5 },
        intervalleSuivi: 5
    }, window.BabaTransCarte || {});

    // Même présentation que les pages du serveur : séparateur de milliers français, point décimal (voir Program.cs).
    const formatNombre = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 1 });
    const nombre = { format: (valeur) => formatNombre.format(valeur).replace(',', '.') };
    const montant = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 });
    const ESPACE = '\u00a0'; // espace insécable : « ± 12 m » ne se coupe pas en fin de ligne

    /** Heure de l'appareil de l'utilisateur, décalée de quelques secondes ou minutes. */
    function heureDecalee(secondes, avecSecondes) {
        return new Date(Date.now() + secondes * 1000).toLocaleTimeString('fr-FR',
            avecSecondes ? { hour: '2-digit', minute: '2-digit', second: '2-digit' } : { hour: '2-digit', minute: '2-digit' });
    }

    // ---------------------------------------------------------------------------
    // Outils communs
    // ---------------------------------------------------------------------------

    function creerCarte(element, centre, zoom) {
        const carte = L.map(element, { scrollWheelZoom: false })
            .setView(centre || config.centre, zoom || config.zoom);
        L.tileLayer(config.tuiles, { maxZoom: 19, attribution: config.attribution }).addTo(carte);
        // La molette ne zoome qu'après un clic sur la carte : la page reste facile à faire défiler.
        carte.once('focus', () => carte.scrollWheelZoom.enable());
        return carte;
    }

    const ICONES = {
        depart: 'bi-shop',
        depot: 'bi-building',
        destination: 'bi-geo-alt-fill',
        livreur: 'bi-truck'
    };

    function icone(type, inactif) {
        const rond = type === 'livreur';
        return L.divIcon({
            className: 'bt-map-icone',
            html: `<span class="bt-map-pin bt-map-pin-${type}${inactif ? ' bt-map-pin-inactif' : ''}"><i class="bi ${ICONES[type]}"></i></span>`,
            iconSize: [36, 36],
            iconAnchor: rond ? [18, 18] : [18, 36],
            popupAnchor: [0, rond ? -18 : -34]
        });
    }

    /** Contenu de bulle construit sans HTML : les noms saisis par les utilisateurs ne sont jamais interprétés. */
    function bulle(titre, lignes) {
        const div = document.createElement('div');
        const fort = document.createElement('strong');
        fort.textContent = titre;
        div.appendChild(fort);
        (lignes || []).filter(Boolean).forEach((texte) => {
            const ligne = document.createElement('div');
            ligne.className = 'small text-muted';
            ligne.textContent = texte;
            div.appendChild(ligne);
        });
        return div;
    }

    function lirePoint(texte) {
        if (!texte) return null;
        const [lat, lng] = texte.split(',').map(Number.parseFloat);
        return Number.isFinite(lat) && Number.isFinite(lng) ? L.latLng(lat, lng) : null;
    }

    function distanceKm(a, b) {
        const rad = (d) => d * Math.PI / 180;
        const dLat = rad(b.lat - a.lat);
        const dLng = rad(b.lng - a.lng);
        const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2;
        return 2 * 6371 * Math.asin(Math.min(1, Math.sqrt(h)));
    }

    function formaterDuree(minutes) {
        if (minutes == null) return '—';
        if (minutes < 60) return `${minutes} min`;
        const heures = Math.floor(minutes / 60);
        const reste = minutes % 60;
        return reste ? `${heures} h ${String(reste).padStart(2, '0')}` : `${heures} h`;
    }

    function formaterAge(secondes) {
        if (secondes == null) return '—';
        if (secondes < 60) return `il y a ${Math.max(1, Math.round(secondes))} s`;
        if (secondes < 3600) return `il y a ${Math.round(secondes / 60)} min`;
        if (secondes < 86400) return `il y a ${Math.round(secondes / 3600)} h`;
        return `il y a ${Math.round(secondes / 86400)} j`;
    }

    function ecrire(bloc, champ, texte) {
        bloc.querySelectorAll(`[data-champ="${champ}"]`).forEach((el) => { el.textContent = texte; });
    }

    function differer(fonction, delai) {
        let minuterie;
        return (...args) => {
            clearTimeout(minuterie);
            minuterie = setTimeout(() => fonction(...args), delai);
        };
    }

    /** Appel JSON vers l'application. Une page HTML en réponse signifie que la session a expiré. */
    async function lireJson(url, options) {
        const reponse = await fetch(url, Object.assign({ credentials: 'same-origin', headers: { Accept: 'application/json' } }, options || {}));
        let donnees = null;
        if ((reponse.headers.get('Content-Type') || '').includes('application/json')) {
            donnees = await reponse.json();
        }
        if (!reponse.ok || donnees === null) {
            const erreur = new Error((donnees && donnees.message)
                || (reponse.ok ? 'Session expirée : rechargez la page.' : `Erreur ${reponse.status}`));
            erreur.statut = reponse.status;
            erreur.donnees = donnees;
            throw erreur;
        }
        return donnees;
    }

    function messageErreurGps(erreur) {
        if (!window.isSecureContext) {
            return 'La géolocalisation exige une connexion sécurisée (adresse en https://).';
        }
        switch (erreur && erreur.code) {
            case 1: return 'Accès à la position refusé : autorisez la localisation pour ce site dans le navigateur.';
            case 2: return 'Position indisponible : activez le GPS du téléphone.';
            case 3: return 'Le GPS met trop de temps à répondre. Réessayez à l\'extérieur.';
            default: return 'Impossible d\'obtenir la position.';
        }
    }

    // ---------------------------------------------------------------------------
    // Géocodage (adresse ↔ coordonnées) avec le service Nominatim d'OpenStreetMap
    // ---------------------------------------------------------------------------

    async function rechercherAdresses(texte) {
        const url = `${config.geocodage}/search?format=jsonv2&addressdetails=1&limit=5&countrycodes=cd&accept-language=fr&q=${encodeURIComponent(texte)}`;
        const reponse = await fetch(url, { headers: { Accept: 'application/json' } });
        if (!reponse.ok) throw new Error('Recherche indisponible');
        return reponse.json();
    }

    async function adresseDuPoint(latlng) {
        const url = `${config.geocodage}/reverse?format=jsonv2&addressdetails=1&zoom=18&accept-language=fr&lat=${latlng.lat}&lon=${latlng.lng}`;
        const reponse = await fetch(url, { headers: { Accept: 'application/json' } });
        if (!reponse.ok) throw new Error('Adresse indisponible');
        const resultat = await reponse.json();
        return resultat.address || {};
    }

    function decomposerAdresse(adresse) {
        const a = adresse || {};
        const rue = a.road ? (a.house_number ? `${a.road} n°${a.house_number}` : a.road) : (a.amenity || a.shop || a.building || '');
        return {
            rue,
            quartier: a.suburb || a.neighbourhood || a.quarter || a.city_district || '',
            ville: a.city || a.town || a.municipality || a.village || a.county || a.state || ''
        };
    }

    // ---------------------------------------------------------------------------
    // Choix d'un point sur la carte (clic, déplacement du repère, recherche, position du téléphone)
    // ---------------------------------------------------------------------------

    function creerSelecteurPoint(bloc, options) {
        const champLatitude = bloc.querySelector('[data-role="latitude"]');
        const champLongitude = bloc.querySelector('[data-role="longitude"]');
        const zoneMessage = bloc.querySelector('[data-role="message"]');
        const carte = creerCarte(bloc.querySelector('[data-role="carte"]'), lirePoint(bloc.dataset.centre), options.zoomInitial);
        let repere = null;

        function message(texte) {
            if (!zoneMessage) return;
            zoneMessage.textContent = texte || '';
            zoneMessage.hidden = !texte;
        }

        function point() {
            const lat = Number.parseFloat(champLatitude.value);
            const lng = Number.parseFloat(champLongitude.value);
            return Number.isFinite(lat) && Number.isFinite(lng) ? L.latLng(lat, lng) : null;
        }

        function placer(latlng, infos) {
            infos = infos || {};
            champLatitude.value = latlng.lat.toFixed(6);
            champLongitude.value = latlng.lng.toFixed(6);
            if (!repere) {
                repere = L.marker(latlng, { icon: icone(options.typeRepere), draggable: true, title: options.titreRepere }).addTo(carte);
                repere.on('dragend', () => placer(repere.getLatLng(), { source: 'deplacement' }));
            } else {
                repere.setLatLng(latlng);
            }
            if (infos.zoom) carte.setView(latlng, infos.zoom);
            if (infos.source !== 'ville') message('');
            if (options.onChange) options.onChange(latlng, infos);
        }

        function effacer() {
            champLatitude.value = '';
            champLongitude.value = '';
            if (repere) {
                carte.removeLayer(repere);
                repere = null;
            }
            if (options.onChange) options.onChange(null, { source: 'effacement' });
        }

        carte.on('click', (e) => placer(e.latlng, { source: 'clic' }));

        // Recherche d'adresse
        const champRecherche = bloc.querySelector('[data-role="recherche"]');
        const listeResultats = bloc.querySelector('[data-role="resultats"]');
        function ligneResultat(texte) {
            const div = document.createElement('div');
            div.className = 'bt-map-resultat text-muted';
            div.textContent = texte;
            listeResultats.replaceChildren(div);
            listeResultats.hidden = false;
        }
        async function rechercher() {
            const texte = champRecherche.value.trim();
            if (texte.length < 3) {
                ligneResultat('Saisissez au moins 3 caractères.');
                return;
            }
            ligneResultat('Recherche en cours...');
            try {
                const resultats = await rechercherAdresses(texte);
                if (!resultats.length) {
                    ligneResultat('Aucune adresse trouvée : cliquez directement sur la carte.');
                    return;
                }
                listeResultats.replaceChildren(...resultats.map((resultat) => {
                    const bouton = document.createElement('button');
                    bouton.type = 'button';
                    bouton.className = 'bt-map-resultat';
                    bouton.textContent = resultat.display_name;
                    bouton.addEventListener('click', () => {
                        listeResultats.hidden = true;
                        placer(L.latLng(Number.parseFloat(resultat.lat), Number.parseFloat(resultat.lon)),
                            { source: 'recherche', zoom: 16, adresse: resultat.address });
                    });
                    return bouton;
                }));
            } catch {
                ligneResultat('Recherche indisponible : cliquez directement sur la carte.');
            }
        }
        if (champRecherche) {
            bloc.querySelector('[data-role="rechercher"]').addEventListener('click', rechercher);
            // Entrée lance la recherche au lieu d'envoyer le formulaire.
            champRecherche.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    rechercher();
                }
            });
        }

        // Position actuelle du téléphone ou de l'ordinateur
        const boutonPosition = bloc.querySelector('[data-role="ma-position"]');
        if (boutonPosition) {
            boutonPosition.hidden = !('geolocation' in navigator);
            boutonPosition.addEventListener('click', () => {
                boutonPosition.disabled = true;
                navigator.geolocation.getCurrentPosition(
                    (position) => {
                        boutonPosition.disabled = false;
                        placer(L.latLng(position.coords.latitude, position.coords.longitude), { source: 'gps', zoom: 17 });
                    },
                    (erreur) => {
                        boutonPosition.disabled = false;
                        message(messageErreurGps(erreur));
                    },
                    { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 });
            });
        }

        const boutonEffacer = bloc.querySelector('[data-role="effacer"]');
        if (boutonEffacer) boutonEffacer.addEventListener('click', effacer);

        const initial = point();
        if (initial) placer(initial, { source: 'initial', zoom: 15 });

        return { carte, point, placer, message };
    }

    // ---------------------------------------------------------------------------
    // Formulaire de commande : point de livraison, adresse, distance et tarif en direct
    // ---------------------------------------------------------------------------

    function initFormulaireCommande(bloc) {
        const formulaire = bloc.closest('form');
        const champ = (id) => formulaire.querySelector(`#${id}`);
        const champPoids = champ('PoidsEstimeKg');
        const champClient = champ('ClientId');
        const champVille = champ('VilleDestination');
        const champAdresse = champ('AdresseDestination');
        const champQuartier = champ('QuartierDestination');
        const panneau = bloc.querySelector('[data-role="estimation"]');
        const villes = JSON.parse(bloc.dataset.villes || '{}');
        let calqueItineraire = null;
        let repereDepart = null;
        let numeroRequete = 0;
        let pointApproximatif = false;
        let recadrer = true;

        // Adresse, quartier et ville sont pré-remplis depuis le point choisi ; l'utilisateur peut les corriger.
        let numeroAdresse = 0;
        const completerAdresse = differer(async (latlng, adresseConnue) => {
            const numero = ++numeroAdresse;
            try {
                const adresse = decomposerAdresse(adresseConnue || await adresseDuPoint(latlng));
                if (numero !== numeroAdresse) return;
                if (adresse.ville && champVille) champVille.value = adresse.ville;
                if (champAdresse) champAdresse.value = adresse.rue;
                if (champQuartier) champQuartier.value = adresse.quartier;
            } catch {
                // Adresse introuvable : les champs restent à compléter à la main.
            }
        }, 600);

        const estimer = differer(async () => {
            const point = selecteur.point();
            if (!point) {
                afficherVide('Placez le point de livraison sur la carte.');
                return;
            }
            const parametres = new URLSearchParams({ latitude: point.lat.toFixed(6), longitude: point.lng.toFixed(6) });
            const poids = Number.parseFloat(champPoids && champPoids.value);
            if (Number.isFinite(poids) && poids > 0) parametres.set('poids', String(poids));
            if (champClient && champClient.value) parametres.set('clientId', champClient.value);
            if (bloc.dataset.commandeId) parametres.set('commandeId', bloc.dataset.commandeId);

            const numero = ++numeroRequete;
            panneau.classList.add('bt-chargement');
            try {
                const resultat = await lireJson(`${bloc.dataset.urlEstimation}?${parametres}`);
                if (numero === numeroRequete) afficher(resultat);
            } catch (erreur) {
                if (numero === numeroRequete) afficherVide(erreur.message);
            } finally {
                if (numero === numeroRequete) panneau.classList.remove('bt-chargement');
            }
        }, 350);

        // Le sélecteur est créé en dernier : il place aussitôt le point déjà enregistré, ce qui lance l'estimation.
        const selecteur = creerSelecteurPoint(bloc, {
            typeRepere: 'destination',
            titreRepere: 'Point de livraison',
            onChange: (latlng, infos) => {
                pointApproximatif = infos.source === 'ville';
                // Recadrage sur tout l'itinéraire, sauf quand l'utilisateur ajuste le point à la main.
                recadrer = !['clic', 'deplacement'].includes(infos.source);
                if (latlng && ['clic', 'deplacement', 'recherche', 'gps'].includes(infos.source)) {
                    completerAdresse(latlng, infos.adresse);
                }
                estimer();
            }
        });

        // Ville saisie sans point sur la carte : on se place au centre de la ville (position approximative).
        const normaliser = (texte) => (texte || '').trim().normalize('NFD').replace(/[\u0300-\u036f]/g, '').toUpperCase();
        if (champVille) {
            champVille.addEventListener('change', () => {
                if (selecteur.point() && !pointApproximatif) return;
                const ville = Object.keys(villes).find((nom) => normaliser(nom) === normaliser(champVille.value));
                if (!ville) return;
                selecteur.placer(L.latLng(villes[ville][0], villes[ville][1]), { source: 'ville', zoom: 12 });
                selecteur.message(`Position approximative (centre de ${ville}) : précisez le point de livraison sur la carte.`);
            });
        }

        function afficherVide(texte) {
            ['distance', 'duree', 'forfait', 'tarif-poids', 'tarif-distance', 'ajustement'].forEach((c) => ecrire(panneau, c, '—'));
            ecrire(panneau, 'total', '—');
            ecrire(panneau, 'detail', texte);
            ecrire(panneau, 'methode', '');
        }

        function afficher(resultat) {
            const carte = selecteur.carte;
            const depart = L.latLng(resultat.depart.latitude, resultat.depart.longitude);
            if (!repereDepart) {
                repereDepart = L.marker(depart, { icon: icone('depart'), title: 'Point de départ' }).addTo(carte);
            }
            repereDepart.setLatLng(depart).bindPopup(bulle('Départ', [resultat.depart.libelle]));

            if (calqueItineraire) carte.removeLayer(calqueItineraire);
            calqueItineraire = L.polyline(resultat.trace, resultat.routier
                ? { color: '#0d6efd', weight: 5, opacity: 0.75 }
                : { color: '#0d6efd', weight: 3, opacity: 0.7, dashArray: '8 8' }).addTo(carte);
            if (recadrer) carte.fitBounds(calqueItineraire.getBounds().extend(depart), { padding: [40, 40], maxZoom: 16, animate: false });

            ecrire(panneau, 'distance', `${nombre.format(resultat.distanceKm)} km`);
            ecrire(panneau, 'duree', formaterDuree(resultat.dureeMinutes));
            ecrire(panneau, 'methode', resultat.routier ? 'Distance par la route' : 'Distance estimée (ligne droite corrigée)');

            if (resultat.tarif) {
                ecrire(panneau, 'forfait', `${montant.format(resultat.tarif.forfait)} FC`);
                ecrire(panneau, 'tarif-poids', `${montant.format(resultat.tarif.poids)} FC`);
                ecrire(panneau, 'tarif-distance', `${montant.format(resultat.tarif.distance)} FC`);
                ecrire(panneau, 'libelle-ajustement', resultat.tarif.libelleAjustement);
                ecrire(panneau, 'ajustement', resultat.tarif.ajustement > 0 ? `+ ${montant.format(resultat.tarif.ajustement)} FC` : '—');
                ecrire(panneau, 'total', `${montant.format(resultat.tarif.total)} FC`);
                ecrire(panneau, 'detail', resultat.tarif.poidsReel
                    ? `Poids réel des colis (${nombre.format(resultat.tarif.poidsKg)} kg) · départ : ${resultat.depart.libelle}`
                    : `Départ : ${resultat.depart.libelle}`);
            } else {
                ['forfait', 'tarif-poids', 'tarif-distance', 'ajustement', 'total'].forEach((c) => ecrire(panneau, c, '—'));
                ecrire(panneau, 'detail', 'Indiquez le poids estimé pour obtenir le montant.');
            }
        }

        if (champPoids) champPoids.addEventListener('input', () => { recadrer = false; estimer(); });
        if (champClient) champClient.addEventListener('change', () => { recadrer = true; estimer(); });
        if (!selecteur.point()) afficherVide('Placez le point de livraison sur la carte.');
    }

    // Formulaire supermarché : simple choix de l'emplacement.
    function initSelecteurSimple(bloc) {
        creerSelecteurPoint(bloc, { typeRepere: 'depart', titreRepere: 'Emplacement du supermarché' });
    }

    // ---------------------------------------------------------------------------
    // Carte de consultation : départ → destination, avec le tracé routier si disponible
    // ---------------------------------------------------------------------------

    function initCarteItineraire(bloc) {
        const depart = lirePoint(bloc.dataset.depart);
        const destination = lirePoint(bloc.dataset.destination);
        const carte = creerCarte(bloc.querySelector('[data-role="carte"]'), destination || depart, 14);
        const limites = L.latLngBounds([]);

        if (depart) {
            L.marker(depart, { icon: icone('depart') }).addTo(carte).bindPopup(bulle('Départ', [bloc.dataset.libelleDepart]));
            limites.extend(depart);
        }
        if (destination) {
            L.marker(destination, { icon: icone('destination') }).addTo(carte).bindPopup(bulle('Livraison', [bloc.dataset.libelleDestination]));
            limites.extend(destination);
        }
        if (!depart || !destination) return;

        const ligneDroite = L.polyline([depart, destination], { color: '#0d6efd', weight: 3, opacity: 0.6, dashArray: '8 8' }).addTo(carte);
        carte.fitBounds(limites, { padding: [40, 40], maxZoom: 15, animate: false });

        if (!bloc.dataset.urlItineraire) return;
        const parametres = new URLSearchParams({
            departLatitude: depart.lat, departLongitude: depart.lng,
            arriveeLatitude: destination.lat, arriveeLongitude: destination.lng
        });
        lireJson(`${bloc.dataset.urlItineraire}?${parametres}`)
            .then((resultat) => {
                if (!resultat.routier) return;
                carte.removeLayer(ligneDroite);
                L.polyline(resultat.trace, { color: '#0d6efd', weight: 5, opacity: 0.75 }).addTo(carte);
            })
            .catch(() => { /* la ligne droite reste affichée */ });
    }

    // ---------------------------------------------------------------------------
    // Suivi d'une livraison en temps réel (personnel, supermarché, livreur)
    // ---------------------------------------------------------------------------

    function initSuiviLivraison(bloc) {
        const carte = creerCarte(bloc.querySelector('[data-role="carte"]'));
        const intervalle = Math.max(3, Number.parseInt(bloc.dataset.intervalle || config.intervalleSuivi, 10)) * 1000;
        const boutonRecentrer = bloc.querySelector('[data-role="recentrer"]');
        const etat = bloc.querySelector('[data-role="etat"]');
        const trace = L.polyline([], { color: '#0d6efd', weight: 4, opacity: 0.8 }).addTo(carte);
        let repereLivreur = null;
        let cerclePrecision = null;
        let repereDepart = null;
        let repereDestination = null;
        let suivreLivreur = true;
        let premierAffichage = true;
        let termine = false;
        let minuterie = null;

        // Si l'utilisateur déplace la carte, on arrête de recentrer automatiquement sur le livreur.
        carte.on('dragstart', () => {
            suivreLivreur = false;
            if (boutonRecentrer) boutonRecentrer.hidden = false;
        });
        if (boutonRecentrer) {
            boutonRecentrer.addEventListener('click', () => {
                suivreLivreur = true;
                boutonRecentrer.hidden = true;
                if (repereLivreur) carte.setView(repereLivreur.getLatLng(), Math.max(carte.getZoom(), 14));
            });
        }

        function placerLivreur(latlng, precision, actif, details) {
            if (!repereLivreur) {
                repereLivreur = L.marker(latlng, { icon: icone('livreur', !actif), zIndexOffset: 1000, title: details ? details.titre : 'Livreur' }).addTo(carte);
                cerclePrecision = L.circle(latlng, { radius: precision || 0, color: '#0d6efd', weight: 1, fillOpacity: 0.08 }).addTo(carte);
            } else {
                repereLivreur.setLatLng(latlng).setIcon(icone('livreur', !actif));
                cerclePrecision.setLatLng(latlng).setRadius(precision || 0);
            }
            if (details) repereLivreur.bindPopup(bulle(details.titre, details.lignes));
            if (suivreLivreur && actif && !premierAffichage) carte.panTo(latlng);
        }

        function texteEtat(donnees) {
            switch (donnees.statut) {
                case 'EnAttente': return ['attente', 'Départ pas encore scanné'];
                case 'Livree': return ['termine', 'Livraison terminée'];
                case 'Echouee': return ['termine', 'Livraison échouée'];
                default:
                    if (!donnees.position) return ['attente', 'En attente du premier signal GPS'];
                    return donnees.gpsActif ? ['actif', 'GPS actif · en direct'] : ['perdu', 'Signal GPS perdu'];
            }
        }

        function afficher(donnees) {
            if (donnees.depart && !repereDepart) {
                repereDepart = L.marker([donnees.depart.latitude, donnees.depart.longitude], { icon: icone('depart') })
                    .addTo(carte).bindPopup(bulle('Départ'));
            }
            if (donnees.destination && !repereDestination) {
                repereDestination = L.marker([donnees.destination.latitude, donnees.destination.longitude], { icon: icone('destination') })
                    .addTo(carte).bindPopup(bulle('Livraison', [donnees.destination.libelle]));
            }
            trace.setLatLngs(donnees.trace);

            const position = donnees.position;
            if (position) {
                placerLivreur(L.latLng(position.latitude, position.longitude), position.precision, donnees.gpsActif, {
                    titre: donnees.livreur || 'Livreur',
                    lignes: [donnees.vehicule, `Position ${formaterAge(position.ageSecondes)}`]
                });
            }

            const [codeEtat, libelleEtat] = texteEtat(donnees);
            if (etat) {
                etat.dataset.etat = codeEtat;
                etat.textContent = libelleEtat;
            }
            ecrire(bloc, 'maj', position ? `${formaterAge(position.ageSecondes)} (${heureDecalee(-position.ageSecondes, true)})` : '—');
            ecrire(bloc, 'vitesse', position && position.vitesse != null ? `${nombre.format(position.vitesse)} km/h` : '—');
            ecrire(bloc, 'precision', position && position.precision != null ? `±${ESPACE}${Math.round(position.precision)}${ESPACE}m` : '—');
            ecrire(bloc, 'distance-restante', donnees.distanceRestanteKm != null ? `${nombre.format(donnees.distanceRestanteKm)} km` : '—');
            ecrire(bloc, 'arrivee', donnees.dureeRestanteMinutes != null
                ? `vers ${heureDecalee(donnees.dureeRestanteMinutes * 60, false)} (≈${ESPACE}${formaterDuree(donnees.dureeRestanteMinutes)})`
                : '—');

            if (premierAffichage) {
                const limites = L.latLngBounds([]);
                [repereDepart, repereDestination, repereLivreur].forEach((r) => { if (r) limites.extend(r.getLatLng()); });
                if (donnees.trace.length) limites.extend(trace.getBounds());
                if (limites.isValid()) carte.fitBounds(limites, { padding: [40, 40], maxZoom: 15, animate: false });
                premierAffichage = false;
            }
        }

        async function actualiser() {
            if (!document.hidden) {
                try {
                    const donnees = await lireJson(bloc.dataset.urlSuivi);
                    afficher(donnees);
                    termine = donnees.statut === 'Livree' || donnees.statut === 'Echouee';
                } catch (erreur) {
                    if (etat) {
                        etat.dataset.etat = 'perdu';
                        etat.textContent = erreur.statut ? erreur.message : 'Connexion interrompue, nouvelle tentative...';
                    }
                }
            }
            if (!termine) minuterie = setTimeout(actualiser, intervalle);
        }

        // Retour sur l'onglet : actualisation immédiate.
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden && !termine) {
                clearTimeout(minuterie);
                actualiser();
            }
        });

        // Sur le téléphone du livreur, sa propre position s'affiche sans attendre le serveur.
        document.addEventListener('bt:position-locale', (e) => {
            placerLivreur(L.latLng(e.detail.latitude, e.detail.longitude), e.detail.precision, true, null);
        });

        actualiser();
    }

    // ---------------------------------------------------------------------------
    // Partage de la position par le téléphone du livreur
    // ---------------------------------------------------------------------------

    function initPartagePosition(bloc) {
        const boutonDemarrer = bloc.querySelector('[data-role="demarrer"]');
        const boutonArreter = bloc.querySelector('[data-role="arreter"]');
        const zoneMessage = bloc.querySelector('[data-role="message"]');
        const jeton = bloc.querySelector('input[name="__RequestVerificationToken"]').value;
        const cleMemoire = `bt-partage-gps-${bloc.dataset.livraison}`;
        let surveillance = null;
        let derniereLecture = null;
        let dernierEnvoi = 0;
        let dernierPointEnvoye = null;
        let envoiEnCours = false;
        let envoiPlanifie = null;
        let verrouEcran = null;
        let battement = null;

        function message(texte, important) {
            zoneMessage.textContent = texte || '';
            zoneMessage.classList.toggle('text-danger', !!important);
        }

        function memoriser(actif) {
            try {
                if (actif) localStorage.setItem(cleMemoire, '1');
                else localStorage.removeItem(cleMemoire);
            } catch { /* stockage indisponible : pas de reprise automatique */ }
        }

        function afficherEtat(actif) {
            boutonDemarrer.hidden = actif;
            boutonArreter.hidden = !actif;
            bloc.classList.toggle('bt-partage-actif', actif);
        }

        // Empêche l'écran de s'éteindre : un téléphone en veille cesse d'envoyer sa position.
        async function garderEcranAllume() {
            try {
                if ('wakeLock' in navigator && document.visibilityState === 'visible') {
                    verrouEcran = await navigator.wakeLock.request('screen');
                }
            } catch { /* non pris en charge : sans conséquence */ }
        }

        async function envoyer(lecture) {
            if (envoiEnCours) return;
            envoiEnCours = true;
            const coords = lecture.coords;
            const donnees = new URLSearchParams({
                latitude: coords.latitude.toFixed(6),
                longitude: coords.longitude.toFixed(6)
            });
            if (Number.isFinite(coords.accuracy)) donnees.set('precision', coords.accuracy.toFixed(1));
            if (Number.isFinite(coords.speed)) donnees.set('vitesse', coords.speed.toFixed(2));
            if (Number.isFinite(coords.heading)) donnees.set('cap', coords.heading.toFixed(0));

            try {
                const reponse = await lireJson(bloc.dataset.urlEnvoi, {
                    method: 'POST',
                    body: donnees,
                    headers: { Accept: 'application/json', RequestVerificationToken: jeton }
                });
                dernierEnvoi = Date.now();
                dernierPointEnvoye = { lat: coords.latitude, lng: coords.longitude };
                message(reponse.enregistre
                    ? `Position partagée à ${heureDecalee(0, true)} · précision${ESPACE}±${ESPACE}${Math.round(coords.accuracy)}${ESPACE}m`
                    : `Position partagée · précision${ESPACE}±${ESPACE}${Math.round(coords.accuracy)}${ESPACE}m`);
            } catch (erreur) {
                if (erreur.statut === 409 || erreur.statut === 403) {
                    arreter();
                    message(erreur.message, true);
                } else {
                    message('Envoi impossible pour le moment (réseau ?). Nouvel essai automatique.', true);
                }
            } finally {
                envoiEnCours = false;
            }
            // Une position plus récente est arrivée pendant l'envoi : elle part à son tour.
            if (surveillance !== null && derniereLecture !== lecture) planifierEnvoi();
        }

        // Envoi toutes les 10 s, ou 3 s après le précédent si le livreur a bougé de plus de 30 m.
        // Une position trop rapprochée n'est pas perdue : son envoi est simplement reporté.
        function planifierEnvoi() {
            if (envoiEnCours || envoiPlanifie !== null || !derniereLecture) return;
            const coords = derniereLecture.coords;
            const deplacementMetres = dernierPointEnvoye
                ? distanceKm(dernierPointEnvoye, { lat: coords.latitude, lng: coords.longitude }) * 1000
                : Infinity;
            const attente = (deplacementMetres >= 30 ? 3000 : 10000) - (Date.now() - dernierEnvoi);
            envoiPlanifie = setTimeout(() => {
                envoiPlanifie = null;
                if (surveillance !== null) envoyer(derniereLecture);
            }, Math.max(0, attente));
        }

        function surPosition(lecture) {
            derniereLecture = lecture;
            document.dispatchEvent(new CustomEvent('bt:position-locale', {
                detail: { latitude: lecture.coords.latitude, longitude: lecture.coords.longitude, precision: lecture.coords.accuracy }
            }));
            planifierEnvoi();
        }

        function surErreur(erreur) {
            // Une coupure passagère du GPS (tunnel, bâtiment) n'arrête pas le partage : seul un refus d'accès l'arrête.
            if (erreur.code === 1) {
                arreter();
                message(messageErreurGps(erreur), true);
            } else if (!derniereLecture) {
                message(messageErreurGps(erreur), true);
            }
        }

        function demarrer() {
            if (surveillance !== null) return;
            surveillance = navigator.geolocation.watchPosition(surPosition, surErreur,
                { enableHighAccuracy: true, maximumAge: 5000, timeout: 30000 });
            // Arrêté au feu ou en attente chez le client, le téléphone ne signale plus de mouvement :
            // on renvoie la dernière position toutes les 30 s pour montrer que le suivi est toujours actif.
            battement = setInterval(() => {
                if (derniereLecture && !envoiEnCours && Date.now() - dernierEnvoi > 25000) envoyer(derniereLecture);
            }, 5000);
            memoriser(true);
            afficherEtat(true);
            message('Recherche du signal GPS...');
            garderEcranAllume();
        }

        function arreter() {
            if (surveillance !== null) navigator.geolocation.clearWatch(surveillance);
            surveillance = null;
            clearInterval(battement);
            clearTimeout(envoiPlanifie);
            envoiPlanifie = null;
            memoriser(false);
            afficherEtat(false);
            if (verrouEcran) verrouEcran.release().catch(() => { });
            verrouEcran = null;
            message('Partage de position arrêté.');
        }

        if (!('geolocation' in navigator)) {
            boutonDemarrer.disabled = true;
            message('Ce navigateur ne permet pas la géolocalisation.', true);
            return;
        }
        if (!window.isSecureContext) {
            boutonDemarrer.disabled = true;
            message('Le partage GPS exige une connexion sécurisée : ouvrez l\'application avec une adresse https://.', true);
            return;
        }

        boutonDemarrer.addEventListener('click', demarrer);
        boutonArreter.addEventListener('click', arreter);
        document.addEventListener('visibilitychange', () => {
            if (surveillance !== null && document.visibilityState === 'visible') garderEcranAllume();
        });

        // Le partage reprend tout seul si la page est rechargée pendant la livraison.
        let repriseDemandee = false;
        try { repriseDemandee = localStorage.getItem(cleMemoire) === '1'; } catch { /* stockage indisponible */ }
        if (repriseDemandee) demarrer();
    }

    // ---------------------------------------------------------------------------
    // Carte de la flotte : tous les livreurs en route (personnel)
    // ---------------------------------------------------------------------------

    function initCarteFlotte(bloc) {
        const carte = creerCarte(bloc.querySelector('[data-role="carte"]'), config.vueRdc.centre, config.vueRdc.zoom);
        const liste = bloc.querySelector('[data-role="liste"]');
        const intervalle = Math.max(3, Number.parseInt(bloc.dataset.intervalle || config.intervalleSuivi, 10)) * 1000;
        const reperes = new Map();
        let premierAffichage = true;

        function elementListe(livraison) {
            const lien = document.createElement('a');
            lien.href = livraison.url;
            lien.className = 'bt-flotte-item';

            const entete = document.createElement('div');
            entete.className = 'd-flex justify-content-between align-items-center gap-2';
            const nom = document.createElement('span');
            nom.className = 'fw-semibold text-heading';
            nom.textContent = livraison.livreur;
            const etat = document.createElement('span');
            etat.className = 'bt-gps-etat small';
            etat.dataset.etat = livraison.gpsActif ? 'actif' : livraison.position ? 'perdu' : 'attente';
            etat.textContent = livraison.gpsActif ? 'En direct' : livraison.position ? formaterAge(livraison.position.ageSecondes) : 'Sans signal';
            entete.append(nom, etat);

            const detail = document.createElement('div');
            detail.className = 'small text-muted';
            detail.textContent = `#${livraison.livraisonId} · ${livraison.codeSuivi || ''} → ${livraison.villeDestination || ''}`;
            lien.append(entete, detail);

            if (livraison.position) {
                lien.addEventListener('mouseenter', () => { const r = reperes.get(livraison.livraisonId); if (r) r.openPopup(); });
            }
            return lien;
        }

        function afficher(livraisons) {
            const visibles = new Set();
            livraisons.forEach((livraison) => {
                if (!livraison.position) return;
                visibles.add(livraison.livraisonId);
                const latlng = L.latLng(livraison.position.latitude, livraison.position.longitude);
                let repere = reperes.get(livraison.livraisonId);
                if (!repere) {
                    repere = L.marker(latlng, { title: livraison.livreur }).addTo(carte);
                    reperes.set(livraison.livraisonId, repere);
                }
                repere.setLatLng(latlng).setIcon(icone('livreur', !livraison.gpsActif));

                const contenu = bulle(livraison.livreur, [
                    `${livraison.supermarche} → ${livraison.villeDestination}`,
                    livraison.vehicule,
                    livraison.position.vitesse != null ? `${nombre.format(livraison.position.vitesse)} km/h` : null,
                    `Position ${formaterAge(livraison.position.ageSecondes)}`
                ]);
                const lienDetails = document.createElement('a');
                lienDetails.href = livraison.url;
                lienDetails.textContent = 'Ouvrir la livraison';
                contenu.appendChild(lienDetails);
                repere.bindPopup(contenu);
            });
            reperes.forEach((repere, id) => {
                if (!visibles.has(id)) {
                    carte.removeLayer(repere);
                    reperes.delete(id);
                }
            });

            liste.replaceChildren(...livraisons.map(elementListe));
            if (!livraisons.length) {
                const vide = document.createElement('div');
                vide.className = 'text-muted small p-3 text-center';
                vide.textContent = 'Aucune livraison en cours pour le moment.';
                liste.appendChild(vide);
            }
            ecrire(bloc, 'nombre-en-cours', String(livraisons.length));
            ecrire(bloc, 'nombre-actifs', String(livraisons.filter((l) => l.gpsActif).length));

            if (premierAffichage && reperes.size) {
                const limites = L.latLngBounds([...reperes.values()].map((r) => r.getLatLng()));
                carte.fitBounds(limites, { padding: [50, 50], maxZoom: 13, animate: false });
            }
            premierAffichage = false;
        }

        async function actualiser() {
            if (!document.hidden) {
                try {
                    afficher(await lireJson(bloc.dataset.urlFlotte));
                    ecrire(bloc, 'maj', `Actualisé à ${new Date().toLocaleTimeString('fr-FR')}`);
                } catch (erreur) {
                    ecrire(bloc, 'maj', erreur.statut ? erreur.message : 'Connexion interrompue, nouvelle tentative...');
                }
            }
            setTimeout(actualiser, intervalle);
        }
        actualiser();
    }

    // ---------------------------------------------------------------------------
    // Initialisation automatique
    // ---------------------------------------------------------------------------

    const initialisations = {
        commande: initFormulaireCommande,
        point: initSelecteurSimple,
        itineraire: initCarteItineraire,
        suivi: initSuiviLivraison,
        flotte: initCarteFlotte,
        partage: initPartagePosition
    };

    document.querySelectorAll('[data-bt-carte]').forEach((bloc) => {
        const type = bloc.dataset.btCarte;
        // Sans la bibliothèque de carte, les formulaires restent utilisables (la ville suffit) et le partage GPS fonctionne.
        if (!window.L && type !== 'partage') return;
        if (initialisations[type]) initialisations[type](bloc);
    });
})();
