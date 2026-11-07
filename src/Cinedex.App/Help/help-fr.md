# Aide de Cinedex

## Bienvenue
Cinedex catalogue votre vidéothèque : films et séries sur disques durs et clés USB, DVD, Blu-ray et autres supports. Il retrouve les fiches sur TMDb (résumé, affiche, distribution, genres), lit les infos techniques des fichiers avec MediaInfo, suit ce que vous avez vu et ce que vous avez prêté.

Pour commencer :
- **Réglages > Métadonnées** : collez votre jeton TMDb (gratuit sur themoviedb.org, Paramètres > API > « Jeton d'accès en lecture »).
- **Ajouter depuis le disque** (Ctrl+H) : choisissez un dossier de films, ou des fichiers, puis **Analyser** et **Importer la sélection**.
- Les films sans ambiguïté sont reliés à TMDb ; pour les autres, la fenêtre **À vérifier** vous propose les candidats à la fin.

Cette aide s'ouvre avec **F1** ou le lien **Aide** en bas du bandeau gauche. La zone de recherche, en haut, filtre les rubriques.

## Ajouter des films depuis un disque
**Ajouter depuis le disque** (Ctrl+H) ouvre la fenêtre d'import.
- **Parcourir...** puis **Analyser** : parcourt un dossier (et ses sous-dossiers) à la recherche de vidéos.
- **Choisir des fichiers…** : ajoute un ou plusieurs fichiers précis. Vous pouvez aussi glisser des fichiers ou des dossiers dans la fenêtre.
- Décochez les fichiers à ignorer, puis **Importer la sélection**.

Options de l'import :
- **Identifier avec TMDb dès l'import** : cherche la fiche de chaque film. Sinon, les films sont ajoutés avec leur seul titre, et vous lancerez plus tard « Mettre à jour via TMDb ».
- **Prendre en compte le fichier nfo si existant** (cochée par défaut) : titre, année et identifiants TMDb ou IMDb sont lus dans le fichier .nfo à côté de la vidéo ; décochée, seuls le nom du fichier et du dossier comptent.
- **Infos techniques (MediaInfo)** : lit codecs, résolution, durée, pistes audio et sous-titres.
- **Ignorer ( ) et [ ] dans les noms** : « 17 fois Cécile Cassard (Christophe Honoré - 2002).mp4 » donne le titre « 17 fois Cécile Cassard » ; l'année entre parenthèses est tout de même lue.
- Les termes de diffusion (1080p, x265, TrueHD, VOSTFR, etc.) sont retirés des titres. La liste se modifie dans **Réglages > Import et scan**.

Les fichiers déjà au catalogue ne sont jamais importés deux fois. Les extraits et bandes-annonces (fichiers trop petits, ou nommés sample, trailer…) sont écartés.

## Disques USB et MediaID
Chaque fichier garde le **MediaID**, le nom du disque sur lequel il a été trouvé (celui affiché par « Propriétés » dans Windows).
- Donnez un nom distinct à chacun de vos disques USB : Cinedex les reconnaît ainsi même si leur lettre change (E:, F:…).
- Deux disques différents peuvent contenir un fichier au même chemin : ils ne sont pas confondus.
- Si vous lancez un film dont le disque est débranché, Cinedex vous dit quel disque brancher.
- Le MediaID se modifie dans la fiche du film, onglet **Fichier**, et se cherche avec la recherche multicritère.

## Fiches TMDb et « À vérifier »
Cinedex relie chaque titre à sa fiche TMDb :
- Si le .nfo ou le nom de fichier contient un identifiant TMDb ou IMDb (tt…), la bonne fiche est prise directement. Ces films sont traités en premier.
- Sinon, Cinedex cherche le titre et l'année : un seul résultat à un an près est relié automatiquement.
- Dans tous les autres cas (plusieurs résultats, aucun à la bonne année, film sans année), Cinedex ne devine pas : le titre va dans **À vérifier**.

La fenêtre **À vérifier** s'ouvre d'elle-même à la fin d'un import ou d'une mise à jour, et reste accessible par l'entrée « À vérifier (N) » du bandeau gauche. Pour chaque titre :
- **Choisir** sous le bon candidat (affiche, année, titre original, résumé) ;
- **Voir sur TMDb ↗** pour ouvrir un candidat dans le navigateur, ou **Voir toutes les propositions sur TMDb ↗** ;
- **Autre recherche…** pour chercher par titre original ou identifiant IMDb ;
- **Aucun ne correspond** : le titre reste au catalogue, sans fiche.

Pour un titre isolé : **Lier ou mettre à jour depuis TMDb** (Ctrl+U). Pour plusieurs titres : **Mettre à jour via TMDb** (Ctrl+Maj+U) sur la sélection ou sur tous les titres affichés. Une mise à jour complète les fiches existantes et ne crée jamais de doublon. Un titre **verrouillé** n'est jamais modifié depuis Internet.

## Notes IMDb, Rotten Tomatoes et Metacritic
- **TMDb** : note fournie avec la fiche.
- **IMDb** : lue dans le fichier public officiel d'IMDb (téléchargé au plus une fois par jour) et, si vous avez une clé, via OMDb.
- **Rotten Tomatoes** et **Metacritic** : via OMDb. Créez une clé gratuite sur omdbapi.com et collez-la dans **Réglages > Métadonnées**.

Si une API atteint sa limite pendant un import, Cinedex vous propose de terminer sans elle ou d'interrompre.

## La fiche d'un film
Un clic sur un titre affiche sa fiche dans le panneau de droite, dont la largeur se règle en glissant la poignée qui le sépare de la liste ; un double-clic (ou Alt+E) ouvre la fiche complète, modifiable, avec ses onglets :
- **Fiche** : titres, année, durée, résumé, pays, studio, identifiant IMDb, bande-annonce. Le **numéro TMDb** accepte aussi `tv/322888` pour les téléfilms, que TMDb range dans sa section télévision, et l'adresse d'une page TMDb collée depuis le navigateur.
- **Vous aimerez aussi** : sous la distribution, Cinedex propose des films de **votre** catalogue proches de celui affiché, avec la raison du rapprochement (même saga, même réalisateur, mots-clés ou genres communs). Tout est calculé sur la base, donc hors ligne et immédiat ; seuls les films que vous possédez sont proposés.
- **Suggestions TMDb** : un bouton demande à TMDb ce que les spectateurs de ce film ont aussi aimé. Un seul appel par titre, le résultat est gardé. Les suggestions déjà au catalogue sont cochées et s'ouvrent d'un clic ; les autres s'ajoutent à votre liste de souhaits.
- Le bouton **bande-annonce** du panneau de droite ouvre la vidéo dans votre navigateur. TMDb la rapatrie avec le reste de la fiche ; **Réglages > Métadonnées** propose de compléter d'un coup celles qui manquent.
- **Notes** : votre note et les notes TMDb, IMDb, Rotten Tomatoes, Metacritic.
- **Distribution** : acteurs avec leur photo et leur rôle ; un clic ouvre la fiche de l'acteur. L'équipe (réalisation, scénario, musique) suit.
- **Fichier** : emplacement complet, MediaID, taille, durée, conteneur, codecs vidéo et audio, langues, sous-titres. **Relire les infos techniques** relance MediaInfo. **Ajouter une partie** rattache un CD2, part2… au même film.
- **Exemplaire** : support, boîtier, version, emplacement, prix, et la gestion complète du **prêt** (prêter, rendre, historique).
- **Genres**, **Classement** (collection, tags, groupes) et **Statut** (vu, favori, possédé, verrouillé, contenu adulte, commentaires), avec l'**historique de visionnage** : chaque fois que vous avez vu le film, à sa date.

Les durées s'affichent en heures et minutes.

## Acteurs
Les acteurs de chaque fiche TMDb sont conservés (nombre réglable dans **Réglages > Métadonnées**). Le lien **Acteurs** du bandeau gauche remplace les affiches de la zone centrale par toutes les personnes du catalogue (photo, nom, nombre de titres), avec une recherche en haut ; un clic sur un filtre de la **Bibliothèque** (Tous les titres, Films, Séries…) ou sur **Affiches** ramène les affiches. Un clic sur une personne affiche sa photo, sa biographie et ses films du catalogue dans le bandeau droit (un clic sur un film ramène aux affiches, sur ce film) ; un double-clic ouvre sa fiche complète ; la fiche d'un acteur montre sa biographie, ses liens TMDb et IMDb, et tous ses films présents au catalogue.

## Réalisateurs
Les réalisateurs ont leur fiche, comme les acteurs. Dans la fiche d'un film (onglet **Distribution**) et dans le panneau de droite, la rubrique **Réalisation** les présente avec leur photo : un clic ouvre leur fiche (biographie, dates, lieu de naissance, liens TMDb et IMDb, téléchargés depuis TMDb à la première ouverture) et la liste de **tous leurs films réalisés** présents au catalogue ; un clic sur un film l'affiche dans la liste.

Le lien **Réalisateurs** du bandeau gauche affiche les réalisateurs du catalogue dans la zone centrale, à la place des affiches, classés par nombre de films ; les filtres de la **Bibliothèque** ramènent les affiches. **Télécharger les fiches manquantes** récupère d'un coup la biographie et la photo de ceux qui ne les ont pas encore. Le lien s'affiche ou se masque dans **Réglages > Bandeau gauche**.

## Collections
Une **collection** regroupe les films d'une saga ou d'un coffret (Retour vers le futur, Le Seigneur des anneaux…). Elle est créée à l'import d'un film lié à TMDb qui appartient à une saga, ou à la main dans l'onglet **Classement** de la fiche.

Le lien **Collections** du bandeau gauche ouvre la liste des collections avec, pour chacune, son décor, son affiche, sa présentation et tous les films de la saga dans l'ordre de sortie :
- les films **du catalogue** portent une coche ; un clic affiche le film dans la liste principale ;
- les films **manquants** sont grisés avec leur affiche TMDb ; un clic ouvre leur fiche sur TMDb.

La case **Seulement les collections incomplètes** ne garde que les sagas auxquelles il manque des films. **Actualiser depuis TMDb** relit la fiche de la collection choisie, **Renommer…** change son nom et **Supprimer la collection…** la retire (ses films restent au catalogue). **Tout compléter depuis TMDb** retrouve par leur nom les collections créées à la main ou reprises d'EMDB, puis télécharge les fiches et décors qui manquent.

La gestion des collections s'active ou se désactive dans **Réglages > Général** (activée par défaut). Désactivée, le lien disparaît du bandeau et les fiches de collection ne sont plus téléchargées à l'import.

## Séries
Une série liée à TMDb récupère ses saisons et épisodes. Le bouton **Saisons et épisodes** de la fiche permet de rattacher les fichiers d'un dossier (noms S01E02 ou 1x02), de marquer des épisodes vus et de les lire.

**Suivi des séries** (lien du bandeau gauche, avec le nombre de séries qui ont des épisodes à regarder) montre pour chaque série la progression (épisodes vus sur épisodes diffusés), le **prochain épisode à regarder** — le bouton **Vu** le marque et passe au suivant — et la **prochaine diffusion** annoncée. À droite, le calendrier des épisodes annoncés dans les 60 prochains jours. **Actualiser depuis TMDb** récupère les nouveaux épisodes et dates des séries en cours. Un double-clic sur une série l'affiche dans le catalogue.

## Clic droit sur un film
Un clic droit dans la liste ou sur une affiche ouvre un menu qui agit sur **toute la sélection** : ouvrir la fiche, lire, ouvrir le dossier, vu / pas vu, favori, modifier en lot, mettre à jour via TMDb, mettre à jour les .nfo, relire les infos techniques, vider la fiche, fusionner les films sélectionnés, supprimer. Un clic droit sur un film hors sélection le sélectionne d'abord ; sur un film déjà sélectionné, le lot est conservé.

**Fusionner les films sélectionnés** réunit des fiches que vous désignez vous-même, sans que Cinedex vérifie qu'il s'agit du même film : c'est à vous de le savoir. La fiche gardée récupère tous les fichiers vidéo des autres, rien n'est effacé sur le disque, et la base est sauvegardée avant. Pour les doublons que Cinedex sait reconnaître, passez plutôt par **Réglages > Maintenance**.

## Rechercher et trier
- La zone **Rechercher…** (Ctrl+F) cherche dans les titres et les résumés.
- **Trier par** classe la liste ; le bouton voisin inverse l'ordre.
- **Liste** (Ctrl+1) ou **Affiches** (Ctrl+2) change la présentation.
- **Échap** efface la recherche, le filtre et les critères.

**Recherche multicritère** (Ctrl+Maj+F, ou le lien du bandeau gauche) combine des conditions sur tous les champs : genre, notes, année, durée, acteur, réalisateur, MediaID, codec, pistes audio, prêt, vu… Les conditions se combinent par ET ou par OU, et le nombre de films trouvés s'affiche pendant la saisie.

Exemple : Genre est Comédie, puis Note IMDb > 7, puis Année ≥ 2000.

**Infobulles** : activez « Infobulle au survol d'un film » dans **Réglages > Général** pour afficher, en survolant un film de la liste ou des affiches, son titre, ses genres, sa note IMDb et le début de son synopsis. Elles sont désactivées par défaut.

## Le bandeau gauche
- **Accueil** : la page qui répond à « qu'est-ce que je regarde ce soir ». Elle propose des films jamais vus, bien notés et dont le fichier est lisible tout de suite (le disque est branché), les sagas dont vous avez vu un épisode mais pas le suivant, les derniers ajouts, et des films possédés depuis plus de six mois et jamais vus. Le tirage ne change qu'une fois par jour.
- **Bibliothèque** : vues rapides (vus, favoris, liste courte, prêtés, fichiers vidéo…).
- **Définition** : sous-menu Définition standard, Haute définition, Ultra HD (4K), 3D.
- **Listes** : les listes de référence (le Top 250 IMDb, et les vôtres). Choisir une liste montre les films du catalogue qui en font partie, et un bandeau dit combien il en manque, avec de quoi les ajouter d'un coup à la liste de souhaits.
- **Mes listes** : vos groupes personnels (« Soirée famille », « À revoir »), saisis dans l'onglet Classement de la fiche ou par modification groupée. Ils sont **ordonnés** : ouvrir une liste la montre dans votre ordre, et deux boutons de la barre d'outils montent ou descendent la sélection. Un titre ajouté se range à la fin. Choisir un autre tri (titre, année…) l'emporte le temps qu'on le garde.
- **Parcourir** : sous-menus **Genres**, **Décennies**, **Pays** et **Langues**. Chacun ne liste que ce que le catalogue porte vraiment, donc un clic ne tombe jamais sur une vue vide. Un seul sous-menu agit à la fois : choisir un genre relâche le pays, et revenir sur une vue de la bibliothèque relâche tout.
- En haut, la **Bibliothèque** : Tous les titres, Vus, Pas encore vus, Favoris, Liste courte, Films seulement, Séries seulement, Prêtés, Fichiers vidéo, **Films sans infos** (titres jamais mis à jour depuis TMDb) et le sous-menu Définition.
- En bas : **À vérifier**, **Recherche multicritère**, **Acteurs**, **Réalisateurs**, **Suivi des séries**, **Collections**, **Apparence**, **Réglages** et **Aide**.

Les vues et liens affichés se choisissent dans **Réglages > Bandeau gauche**.

## Prêts
Dans le panneau de droite ou l'onglet **Exemplaire** de la fiche : saisissez l'emprunteur (les noms déjà utilisés sont proposés) et la date, puis **Prêter**. **Marquer comme rendu** clôt le prêt. L'historique garde tous les prêts du titre ; une ligne erronée peut être effacée. La vue **Prêtés** du bandeau liste les titres dehors.

## Fichiers .nfo
Cinedex lit et écrit les fichiers .nfo au format Kodi (Réglages > Fichiers .nfo). Dans le menu « Actions sur la sélection », **Écrire les .nfo** crée ceux qui manquent et complète les champs vides ; **Mettre à jour les .nfo** ne touche qu'aux fichiers existants et y remplace les valeurs, puis propose de créer ceux qui manquent :
- à l'import, un .nfo présent à côté du film complète sa fiche, et ses identifiants servent à trouver la bonne fiche TMDb ;
- après une mise à jour TMDb, le .nfo peut être créé ou complété, avec une copie .nfo.bak de sécurité.

## Apparence, langue et fenêtre
- **Apparence** (bandeau gauche) ou **Réglages > Apparence** : mode clair, sombre ou comme le système.
- **Réglages > Apparence** règle aussi le **jeu d'icônes** (trait fin, trait marqué, deux tons, ou classique Buddha), la **couleur des menus** (bandeau gauche : pierre, ardoise, sauge, lavande, rose poudré, blanc), la **couleur d'accent** (rouge velours, bleu, vert, violet, ambre, graphite) et la **police** (Inter fournie avec Cinedex, police du système, Segoe UI, Calibri, Verdana, Trebuchet MS, Georgia, Arial). Chaque pastille montre la couleur en thème clair et sombre ; tout s'applique à l'enregistrement, sans redémarrer.
- **Langue de l'interface** : français, anglais, chinois, hindi, espagnol, arabe, portugais, russe, allemand, japonais. Elle s'applique au redémarrage, proposé tout de suite.
- **Fenêtre au démarrage** : agrandie à l'écran (par défaut), plein écran, ou taille de la dernière fois. **F11** bascule le plein écran.
- La langue des résumés et genres TMDb se règle à part (Réglages > Métadonnées, fr-FR par défaut).

## Catalogues
Plusieurs catalogues indépendants peuvent coexister (par exemple : films, séries, collection d'un proche). Le menu sous le logo permet d'en ouvrir, créer, renommer ou retirer. Un catalogue retiré est déplacé dans les sauvegardes, jamais effacé.

## Réglages
- **Général** : langue, fenêtre au démarrage, contenu adulte, infobulles, collections, dossier des données, journal des actions.
- **Apparence** : thème, jeu d'icônes, couleur des menus, couleur d'accent, police.
- **Liste des films** : colonnes affichées dans la vue liste (affiche, année, mention « Série », support, durée, genres, note IMDb, ma note, prêt, favori, vu, numéro). Genres et note IMDb sont masqués par défaut.
- **Sauvegardes** et **Export** : voir ci-dessous.
- **Réseau** : proxy pour accéder à Internet (voir ci-dessous).
- **Économiseur d'écran** : activation, durée d'inactivité, intervalle du diaporama, titres sous les affiches.
- **Bandeau gauche** : vues et liens affichés.
- **Import et scan** : extensions vidéo, taille minimale, mots qui font ignorer un fichier, sous-dossiers, termes retirés des titres, options cochées par défaut.
- **Métadonnées** : jeton TMDb, langue et pays, nombre d'acteurs, clé OMDb, fichier IMDb ; un bouton met à jour via TMDb les seuls titres sans fiche TMDb (jamais importés).
- **Fichiers .nfo** et **Lecture et outils** (MediaInfo, lecteur externe, marquer comme vu à la lecture).
- **Maintenance** : entretien de la base du catalogue ouvert (voir ci-dessous).
- **Statistiques** : chiffres du catalogue ouvert (films, séries, épisodes, acteurs, réalisateurs, vus, favoris, prêtés, fichiers, taille des fichiers, durée totale des films, titres liés à TMDb, à vérifier) et répartitions par genre, décennie, définition, support et disque (MediaID).

## Économiseur d'écran
Quand la fenêtre de Cinedex est active mais que ni le clavier ni la souris n'ont servi depuis **5 minutes**, un diaporama plein écran des affiches du catalogue démarre : chaque affiche s'affiche sur le décor flouté du film, avec son titre et son année, puis laisse la place à la suivante en fondu. Les affiches passent dans un ordre aléatoire, toutes une fois avant de recommencer.

Une touche, un clic, la molette ou un mouvement de souris ferme le diaporama. Il ne démarre pas si Cinedex n'est pas au premier plan (par exemple pendant la lecture d'un film dans un autre logiciel) ni si aucun film n'a d'affiche ; les titres marqués « contenu pour adultes » en sont exclus quand ils sont masqués dans la liste.

Tout se règle dans **Réglages > Économiseur d'écran** : activation, durée d'inactivité (1 à 240 minutes), intervalle entre deux affiches (2 à 300 secondes) et affichage du titre sous les affiches.

## Maintenance de la base
L'onglet **Réglages > Maintenance** affiche l'emplacement, la taille et le contenu de la base du catalogue ouvert, et propose :
- **Renommer le catalogue…** : le catalogue ouvert change de nom ; les réglages se ferment et il est rouvert aussitôt.
- **Sauvegarder maintenant** et **Ouvrir le dossier des sauvegardes**.
- **Purger le catalogue** : efface tout le contenu du catalogue ouvert (titres, exemplaires, fichiers, personnes, collections, prêts, séries, images). Ses réglages, listes de référence, champs personnalisés, filtres enregistrés et dossiers à analyser restent. Deux confirmations sont demandées ; la base est sauvegardée juste avant (dossier des sauvegardes), mais pas les images.
- **Retirer les acteurs inutilisés** : personnes qui ne figurent plus dans aucun générique (le nombre est indiqué sur le bouton).
- **Fusionner les doublons d'acteurs et de réalisateurs** : les fiches au nom identique (accents et majuscules ignorés) sont regroupées ; deux homonymes aux fiches TMDb ou IMDb différentes restent distincts.
- **Trouver les films en double** : même fiche TMDb ou même identifiant IMDb ; à défaut d'identifiant, même titre et même année. Rien n'est supprimé ; un double-clic sur un titre l'affiche dans la liste.
- **Fusionner les films en double** : regroupe chaque groupe en une seule fiche, qui garde tous les fichiers vidéo — un même film en plusieurs versions, un fichier par version. Le compte rendu distingue les vraies versions des copies d'un même encodage, d'après MediaInfo.
- **Renuméroter les films selon le tri actuel** : les exemplaires (colonne N°) sont numérotés de 1 à n dans l'ordre de tri de la liste principale (par exemple Titre ↑ ou Date d'ajout ↓).
- **Supprimer les collections, tags et groupes vides** et **Nettoyer les images inutilisées** (affiches, fonds et photos que plus rien n'utilise).
- **Vérifier l'intégrité de la base** et **Compacter et optimiser la base**.

Ces opérations s'appliquent tout de suite, sans « Enregistrer ». Chacune de celles qui modifient la base demande confirmation et fait d'abord une sauvegarde dans le dossier des sauvegardes.

## Raccourcis clavier
- **F1** : aide
- **Ctrl+F** : rechercher ; **Ctrl+Maj+F** : recherche multicritère ; **Échap** : tout afficher
- **Ctrl+H** : ajouter depuis le disque ; **Alt+A** : ajouter un titre ; **Ctrl+T** : chercher sur TMDb
- **Ctrl+U** : lier ou mettre à jour depuis TMDb ; **Ctrl+Maj+U** : mettre à jour via TMDb
- **Alt+E** ou double-clic : modifier la fiche ; **Suppr** : supprimer du catalogue
- **Ctrl+P** : lire ; **Ctrl+O** : ouvrir le dossier du fichier
- **Ctrl+S** : vu / pas vu ; **Ctrl+D** : favori ; **Ctrl+R** ou **F7** : un titre non vu au hasard
- **Ctrl+1** : liste ; **Ctrl+2** : affiches ; **F5** : rafraîchir ; **F11** : plein écran
- Dans la vue affiches : **flèches** pour se déplacer (avec **Maj** pour étendre la sélection), **Début**/**Fin**, **Pg préc.**/**Pg suiv.**, **Entrée** pour ouvrir la fiche, **Ctrl+A** pour tout sélectionner, **Ctrl+clic** et **Maj+clic** pour une sélection multiple.

## Accessibilité
Chaque bouton est annoncé par son nom aux lecteurs d'écran (Narrateur, NVDA, VoiceOver), y compris les boutons à icône seule ; les affiches de la grille sont annoncées par leur titre et leur année. Les couleurs du texte respectent les contrastes recommandés (WCAG AA) en thème clair et sombre, quelle que soit la couleur d'accent ou de menu choisie. Toute la navigation principale se fait au clavier (voir les raccourcis).

## Grands catalogues
Cinedex est conçu pour des catalogues de plus de 10 000 films : seules les lignes et affiches visibles sont créées, les affiches sont décodées en arrière-plan, et marquer un film vu ou favori ne met à jour que sa ligne. Les performances sont vérifiées à chaque version sur un catalogue de 12 000 films.

## Installation et mises à jour
**Cinedex-win-Setup.exe** installe Cinedex pour l'utilisateur (sans droits d'administrateur) avec un raccourci sur le bureau et dans le menu Démarrer. La version installée vérifie au démarrage si une nouvelle version est publiée : un bouton **Mise à jour disponible** apparaît alors dans le bandeau gauche ; elle s'installe en quelques secondes (seuls les fichiers modifiés sont téléchargés) et Cinedex redémarre dessus. **Réglages > Général > Mises à jour** permet de rechercher à la demande ou de désactiver la recherche au démarrage. Les catalogues et réglages ne sont jamais touchés par une mise à jour. La version portable (archive .zip) reste disponible et se met à jour à la main ; Linux (AppImage) et macOS (.pkg) ont aussi leur programme d'installation.

## Sauvegardes
**Réglages > Sauvegardes** crée des sauvegardes complètes du catalogue ouvert : une archive .zip qui contient, au choix, la **base de données** (fiches, notes, prêts, fichiers, réglages du catalogue), les **images** (affiches, fonds, photos, décors) et les **préférences communes** (apparence, langue, fenêtre, journal), avec un manifeste.
- **Sauvegarder maintenant** crée une sauvegarde tout de suite.
- **Sauvegarde automatique** : jamais, à chaque démarrage, une fois par jour, par semaine ou par mois. Elle se fait en arrière-plan pendant que Cinedex est ouvert.
- **Dossier** : par défaut le sous-dossier « dumps » des sauvegardes de Cinedex ; **Choisir…** permet par exemple un disque USB ou un dossier synchronisé.
- **Sauvegardes gardées** : au-delà de ce nombre, les plus anciennes sont effacées (0 : toutes gardées).
- **Restaurer…** remplace la base du catalogue par celle de la sauvegarde choisie (une copie de la base actuelle est gardée d'abord) et ajoute les images manquantes ; les réglages se ferment et le catalogue est rouvert.

## Cinedex sur Android
L'application Android (fichier .apk, à installer à la main, hors Play Store) permet de consulter le catalogue sur un téléphone, en lecture seule : recherche, liste avec affiches et genres, fiche de chaque film (résumé, notes, réalisation, distribution).
1. Sur l'ordinateur, créez une sauvegarde avec la base et les images (**Réglages > Sauvegardes**).
2. Copiez le fichier .zip sur le téléphone (câble USB, cloud, e-mail…).
3. Dans Cinedex sur le téléphone, touchez **Importer une sauvegarde** et choisissez ce fichier. Un nouvel import remplace le catalogue du téléphone.

Pour installer l'APK, autorisez l'installation d'applications provenant de sources inconnues dans les réglages Android.

## Export de la liste
**Réglages > Export** enregistre la liste des films en **CSV**, **TSV** ou **Excel (.xlsx)** : la liste affichée (recherche, filtre et tri actuels) ou tout le catalogue, avec les colonnes cochées (numéro, titre, titre original, année, genres, durée, réalisateurs, acteurs principaux, notes, vu, favori, support, emplacement, collection, prêt, résolution, MediaID, fichiers, taille, identifiants TMDb et IMDb, date d'ajout, synopsis). Le CSV utilise le séparateur de la langue du système (« ; » en français) et s'ouvre directement dans Excel. Colonnes et format sont retenus.

## Proxy
Si votre réseau impose un proxy, activez **Réglages > Réseau > Passer par un proxy**, choisissez **HTTP** ou **HTTPS** et saisissez son adresse de la forme **serveur:port** (par exemple myserver.proxy.com:8080). **Tester la connexion** vérifie que TMDb répond à travers ce proxy. Toutes les connexions de Cinedex (TMDb, OMDb, notes IMDb, images) passent alors par lui ; le changement s'applique à l'enregistrement, sans redémarrer. Désactivé (par défaut), Cinedex suit le proxy configuré dans le système.

## Données, sauvegardes et sécurité
- Les catalogues, images et journaux sont dans le dossier des données (Réglages > Général > Ouvrir le dossier). En mode portable, un fichier portable.flag à côté de Cinedex.exe place les données dans un sous-dossier data.
- Une sauvegarde de la base est faite automatiquement avant chaque mise à jour de son format.
- **Journal des actions** (Réglages > Général, activé par défaut) : chaque action faite dans Cinedex (ajout, modification, suppression, vu, favori, prêt, import, mise à jour TMDb, choix « À vérifier », lecture, catalogues, réglages) est notée avec la date, l'heure et le catalogue, dans un fichier par jour (journal-AAAA-MM-JJ.log, conservé 90 jours). **Exporter le journal du jour…** en fait une copie ; **Ouvrir le dossier des journaux** montre tous les fichiers. Le journal est écrit en français.
- Les clés TMDb et OMDb sont rangées dans le coffre sécurisé du système (Gestionnaire d'identifiants de Windows), jamais dans les fichiers de Cinedex.
- Cinedex ne modifie jamais vos fichiers vidéo. Supprimer un titre du catalogue ne touche pas au disque.

## Nouveautés
### 0.10.0
- **Clic droit sur un film** (bureau) : un menu qui agit sur toute la sélection — fiche, lecture, dossier, vu, favori, modification en lot, mise à jour TMDb, mise à jour des .nfo, infos techniques, vider la fiche, **fusionner les films sélectionnés**, supprimer. Un clic droit hors sélection sélectionne le film visé ; sur un film déjà sélectionné, le lot est gardé.
- **Fusionner une sélection** sans passer par la recherche de doublons : vous désignez les fiches, Cinedex les réunit. Il ne vérifie pas qu'il s'agit du même film et vous en avertit, la base étant sauvegardée avant.
- **Téléfilms trouvés au scan** : quand aucun film ne correspond au nom du fichier, l'import va voir la section télévision de TMDb, où ils vivent. L'entrée reste un film du catalogue. Vos fichiers qui restaient sans fiche devraient maintenant s'identifier seuls.

### 0.9.3
- **Mettre à jour les .nfo ne crée plus rien tout seul.** La commande réécrit les fichiers qui existent, puis, si des films de la sélection n'en ont pas, le dit et vous demande une fois s'il faut les créer. Mettre à jour n'est pas créer, et ne rien dire aurait laissé croire que tout était à jour.

### 0.9.1
- **Mettre à jour les .nfo** : nouvelle entrée du menu « Actions sur la sélection », qui réécrit les .nfo **existants** depuis les fiches de Cinedex en **remplaçant** ce qu'ils contiennent. « Écrire les .nfo » ne faisait que compléter les champs vides, ce qui laissait en place les valeurs devenues fausses. Mettre à jour n'est pas créer : si des films de la sélection n'ont pas de .nfo, Cinedex le dit et vous demande s'il faut les créer. Fonctionne sur la sélection depuis n'importe quelle vue, y compris un disque du sous-menu MediaID, et une copie `.nfo.bak` est faite si le réglage le demande.
- **La fiche ouverte se rafraîchit entière** après une mise à jour depuis TMDb : distribution, équipe, affiche et liste des collections, et plus seulement les champs. Il fallait auparavant la fermer et la rouvrir pour voir la nouvelle distribution.

### 0.9.0
- **Téléfilms** : le numéro TMDb d'une fiche accepte désormais la forme `tv/322888`, et l'adresse d'une page TMDb collée depuis le navigateur. TMDb range les téléfilms dans sa section télévision, avec des numéros indépendants de ceux des films : un numéro seul ne suffisait donc pas à les retrouver. Le titre **reste un film** au catalogue, et la section est retenue avec le numéro — les mises à jour suivantes retournent d'elles-mêmes au bon endroit, au lieu de tomber sur un autre film ou sur rien.
- La recherche TMDb accepte les mêmes formes : `tv/322888`, `movie/949`, ou l'adresse complète.
- Deux titres portant le même numéro dans des sections différentes ne sont plus vus comme des doublons.

### 0.8.4
- **Fusionner un seul groupe de doublons** : choisissez un titre dans la liste, puis **Fusionner ce groupe…**. Les autres groupes ne sont pas touchés. Tous les rapprochements proposés ne se valent pas, et il fallait pouvoir trancher groupe par groupe au lieu de tout prendre ou tout refuser. La liste se relit aussitôt pour ne montrer que ce qui reste à décider.
- L'infobulle de **Trouver les films en double** disait encore « même identifiant IMDb, ou même titre et même année » : la règle est le même numéro TMDb **ou** IMDb, le titre et l'année ne servant qu'à défaut d'identifiant.

### 0.8.3
- **Fusion des doublons** : le texte parlait d'« une version d'encodage par partie ». Ce ne sont pas des parties d'un film découpé, ce sont des **versions** d'un même film, un fichier par version. L'infobulle, l'aide et la fiche le disent maintenant ainsi : un second fichier s'affiche « Fichier 2 » et non « Partie 2 ».
- Cinedex est publié sous **licence MIT**, et la chaîne de publication est prête pour la signature des paquets Windows par la SignPath Foundation : l'avertissement SmartScreen disparaîtra une fois l'adhésion acceptée.

### 0.8.2
- **Un fichier n'est plus perdu quand TMDb ne répond pas pour lui.** Fiche retirée, numéro périmé, erreur du service : le film entrait auparavant dans la colonne « échecs » et n'était pas ajouté du tout. Il est maintenant ajouté **sans fiche**, avec son fichier, et compté dans « sans fiche TMDb ». Le compte rendu nomme le fichier et dit pourquoi. **Mettre à jour via TMDb** permet de réessayer plus tard.
- Le **compte rendu de l'import** se sélectionne et se copie (Ctrl+C), défile quand il est long, et ne cache plus les fichiers signalés au-delà des cinq premiers.

### 0.8.1
- **Listes personnelles ordonnées** : vos listes gardent l'ordre que vous leur donnez. Ouvrir une liste la trie par cet ordre, deux boutons montent ou descendent la sélection (plusieurs titres à la fois gardent leur disposition), et un titre ajouté se range à la fin. Les listes qui existaient déjà partent de l'ordre qu'elles avaient à l'écran, donc rien ne bouge tant que vous ne réordonnez pas.

### 0.8.0
- **Top 250 IMDb** : **Réglages > Métadonnées > Construire le Top 250 IMDb (estimé)** recalcule le classement depuis les fichiers publics d'IMDb, avec la formule officielle (moyenne bayésienne, 25 000 votes minimum). Le premier calcul télécharge environ 200 Mo. La liste est étiquetée **estimé** parce qu'elle l'est : IMDb ne publie pas son classement, seulement sa formule, et applique en plus des pondérations qu'il garde pour lui. Le résultat ressemble beaucoup à la vraie liste sans lui être identique.
- **Ce que la liste dit de votre catalogue** : le sous-menu **Listes** du bandeau gauche montre les films de la liste que vous possédez, et affiche « 187 sur 250 au catalogue, 63 manquant(s) ». Un bouton rapatrie la fiche TMDb de chaque manquant, le marque non possédé et l'étiquette au nom de la liste : vous les retrouvez dans **Liste de souhaits**, et le tag dit d'où ils viennent.
- **Mes listes** : vos groupes personnels sont désormais dans le bandeau gauche, au lieu de n'être accessibles que par la recherche multicritère.
- Le champ **Liste de référence** entre dans la recherche multicritère, combinable avec tout le reste.

### 0.7.0
- **Page d'accueil** : une entrée **Accueil** en haut du bandeau gauche ouvre quatre bandes d'affiches — **À voir ce soir** (jamais vus, bien notés, et le disque est branché), **Reprendre une saga**, **Derniers ajouts** et **Oubliés**. Le tirage tient la journée puis change : une page figée ne fait rien redécouvrir, une page qui bouge sans cesse devient désagréable. Une bande qui n'a rien à montrer ne s'affiche pas.
- **Historique de visionnage** : l'onglet **Statut** de la fiche garde chaque visionnage avec sa date. Revoir un film ajoute un événement au lieu de remplacer le premier, et chaque lecture depuis Cinedex laisse sa trace. Une ligne erronée s'efface, une séance passée s'ajoute à sa date.

### 0.6.1
- **« Vous aimerez aussi »** : la fiche propose des films proches tirés de votre propre catalogue, les plus proches d'abord, chacun avec la raison du rapprochement. Le calcul se fait sur la base (genres, mots-clés, réalisateur, acteurs principaux, saga, époque), donc hors ligne et instantané. Un seul genre partagé ne suffit pas à être proposé : il faut deux genres, ou un genre et un mot-clé, ou le même réalisateur.
- **Mots-clés TMDb** : ils ont désormais leur propre place en base, séparée de vos tags. **Réglages > Métadonnées** propose de les récupérer d'un coup pour les fiches qui n'en ont pas, ce qui rend les rapprochements nettement plus fins.
- **Suggestions TMDb** : sur demande, la fiche affiche ce que TMDb propose autour de ce film. Celles que vous possédez sont cochées, les autres s'ajoutent à la liste de souhaits d'un clic.
- **Bandes-annonces** : un bouton du panneau de droite ouvre la bande-annonce dans le navigateur. Elle était déjà rapatriée de TMDb avec le reste de la fiche, mais rien ne la lisait. Seules les plateformes vidéo connues sont ouvertes. **Réglages > Métadonnées** ajoute « Compléter les bandes-annonces manquantes », qui repasse sur les fiches déjà liées à TMDb qui n'en ont pas.
- **Parcourir le catalogue** : le bandeau gauche gagne quatre sous-menus dépliables, **Genres**, **Décennies**, **Pays** et **Langues**, sur le modèle de MediaID. Ils ne listent que ce que le catalogue porte, les pays et les langues sont nommés et non codés, et les décennies vont de la plus récente à la plus ancienne.

### 0.4.33
- **Mettre à jour une fiche depuis la fiche elle-même** : dans l'éditeur, un bouton **Mettre à jour** à côté du numéro TMDb rapatrie la fiche et recharge le formulaire — descriptif, genres, distribution, affiche. Il suit le numéro affiché : en saisir un autre puis mettre à jour rattache le titre à une autre fiche sans quitter l'éditeur. Les saisies non enregistrées sont remplacées par ce qui arrive de TMDb, et un titre verrouillé n'est pas modifié.

### 0.4.32
- **Les doublons suivent les identifiants** : deux entrées sont un doublon quand elles partagent leur **fiche TMDb ou leur identifiant IMDb**. Deux films qui portent le même titre et la même année mais dont les identifiants diffèrent ne sont plus rapprochés : ce sont des films distincts. Les liens se propagent, donc trois entrées liées deux à deux forment un seul groupe.
- Le titre et l'année ne servent plus que pour les entrées **sans aucun identifiant**, où rien ne peut les contredire : deux fichiers scannés et jamais identifiés continuent de se retrouver.

### 0.4.31
- **MediaID par ordre alphabétique** : le bandeau gauche classe les disques par leur nom, sans tenir compte de la casse, au lieu du nombre de titres de chacun.

### 0.4.30
- **Plusieurs entrées peuvent désigner le même film** : la base n'impose plus qu'un numéro TMDb soit unique, et Cinedex ne refuse plus de créer une fiche déjà présente. Vos deux sources sont donc deux **jumelles complètes**, toutes deux liées à TMDb et actualisables, jusqu'à ce que vous les fusionniez — là où la seconde restait auparavant sans numéro, donc hors de portée de « Mettre à jour via TMDb ».
- **Ajouter un doublon à la main est permis** : ajouter depuis TMDb un film déjà au catalogue crée une nouvelle entrée au lieu de rouvrir l'existante. Les séries gardent une seule entrée, puisqu'elles rassemblent leurs épisodes.
- Cette version **migre le schéma de la base** (une sauvegarde est faite juste avant, dans le dossier des sauvegardes).

### 0.4.29
- **Un fichier, une entrée à l'import** : un fichier vidéo d'un film déjà au catalogue, à un autre emplacement, reçoit désormais sa **propre entrée** au lieu d'être rattaché à la première. Cinedex catalogue des fichiers : c'est **« Fusionner les films en double »** (Réglages > Maintenance) qui regroupe ensuite ces entrées en un seul film à plusieurs sources. La seconde entrée reprend la fiche TMDb (synopsis, genres, affiche, identifiants) ; les deux se retrouvent donc dans les doublons. (Depuis la 0.4.30, elle garde aussi le numéro TMDb.)
- Les **parties annoncées par le nom** (CD1, CD2, part2…) continuent de rejoindre le film qu'elles prolongent, dans l'ordre, quel que soit l'ordre de parcours du dossier.

### 0.4.28
- **Vue MediaID plus lisible** : le bandeau gauche n'affiche plus le nombre de titres à côté de chaque disque, qui tronquait les noms longs.

### 0.4.27
- **Vue par MediaID** : le bandeau gauche a un sous-menu **MediaID** qui liste vos disques. Un clic n'affiche que les films de ce disque ; une vue de la Bibliothèque en ressort.
- **Colonne MediaID** : dans **Réglages > Liste des films**, la colonne « Emplacement » laisse la place au **MediaID**, c'est-à-dire le disque qui porte le fichier. Un titre réparti sur plusieurs disques les affiche tous.

### 0.4.26
- **Plus de colonnes dans la vue liste** : titre original, studio, classification, collection, emplacement et date d'ajout s'ajoutent aux colonnes déjà proposées, à cocher dans **Réglages > Liste des films**. Elles sont lues avec le reste de la ligne, sans ralentir la liste.
- **Exporter la liste affichée** : le menu **•••** de la barre d'outils propose « Exporter la liste affichée… », qui ouvre l'export déjà réglé sur la liste en cours (recherche, filtre et tri actuels). Les colonnes et le format (Excel, CSV, TSV) s'y choisissent.

### 0.4.25
- **Recherche plus fluide sur les gros catalogues** : la frappe ne bloque plus l'interface. La recherche est faite hors du fil d'affichage et une frappe plus récente annule la précédente ; surtout, la liste ne vérifie plus l'existence de chaque affiche sur le disque, ce qui coûtait des milliers d'accès à chaque lettre. Sur un catalogue de 12 000 titres, préparer une recherche passe de 1 484 à 202 ms, et aucune lettre ne retient plus l'affichage.

### 0.4.24
- **Exporter la filmographie d'une personne** : la fiche d'un acteur ou d'un réalisateur a un bouton « Exporter la filmographie » qui enregistre la liste affichée en Excel (.xlsx), CSV ou TSV. Comme la filmographie ne montre que les titres du catalogue, l'export ne porte que sur les films possédés. Les colonnes sont celles de l'export du catalogue.

### 0.4.23
- **Vider la fiche** : un bouton de la fiche de droite, entre « Modifier » et « Lier ou mettre à jour depuis TMDb », efface tout ce qui vient d'une source extérieure (descriptif, identifiants, notes des sites, genres, distribution, affiche). Le titre, vos propres saisies et les fichiers vidéo avec leurs infos MediaInfo sont gardés ; la fiche rejoint « Films sans infos » et se recharge depuis TMDb.
- **Affiche et numéro TMDb modifiables** : dans l'éditeur, l'affiche se change par une image du disque ou se retire, et le numéro TMDb se saisit à la main — vidé, la fiche se détache de TMDb ; changé, elle suit une autre fiche à la prochaine mise à jour.

### 0.4.22
- **Replier la fiche de droite** : elle peut maintenant se replier contre le bord droit, en une bande étroite, pour parcourir les affiches sur presque toute la largeur. Trois façons de le faire : le bouton de la sous-barre (à droite des vues liste et affiches), la touche **F9**, ou tirer la poignée vers la droite au-delà d'un seuil. Le bouton de la bande, un double-clic sur la poignée ou F9 la rouvrent à sa largeur précédente. L'état est mémorisé avec le catalogue.

### 0.4.21
- **Fusionner les films en double** (Réglages > Maintenance) : chaque groupe de doublons devient une seule fiche qui garde tous les fichiers vidéo, un même film en plusieurs versions, un fichier par version. **Fusionner ce groupe…** ne traite que le groupe du titre sélectionné dans la liste, et laisse les autres tels quels. Le compte rendu indique combien de fichiers ont exactement les mêmes caractéristiques MediaInfo qu'un autre du même film, donc lesquels sont de simples copies plutôt qu'une autre version. Aucun fichier du disque n'est touché, et la base est sauvegardée avant.

### 0.4.20
- **Un même film en plusieurs fichiers ne crée plus de doublon** : à l'import, un fichier dont le titre et l'année correspondent à un film déjà au catalogue lui est rattaché comme fichier supplémentaire, au lieu de créer une seconde fiche. Les parties explicites (CD1, CD2…) gardent leur numéro, et un remake d'une autre année reste une fiche à part.

### 0.4.19
- **Réanalyser le même dossier ne recoche plus les fichiers déjà importés** : un fichier déjà rattaché à un titre apparaît marqué « déjà au catalogue » et décoché, donc « Importer la sélection » ne reprend que les nouveaux. Cela vaut même quand le disque est reconnu sous un autre nom, cas où l'ancienne détection laissait passer le fichier.

### 0.4.18
- **« Films sans infos » repose sur le contenu** : la vue (et le bouton de Réglages > Métadonnées) liste les titres qui n'ont ni synopsis ni genre. La date de rapatriement ne servait pas : un .nfo lu au scan la pose aussi, sans rien apporter de descriptif, et ces films restaient invisibles.

### 0.4.17
- **« Films sans infos » corrigé** : la vue (et le bouton de Réglages > Métadonnées) repère maintenant les fiches jamais rapatriées depuis TMDb. Un film dont le numéro TMDb ou IMDb est connu — lu dans un .nfo ou saisi à la main — mais dont la fiche n'a jamais été téléchargée y figure désormais ; avant, il en était exclu à tort.

### 0.4.16
- **Films sans infos** : une vue de la Bibliothèque (bandeau gauche) liste les titres qui n'ont ni synopsis ni genre : scannés, ajoutés à la main, ou identifiés par un .nfo qui n'a donné que des identifiants. Un seul des deux suffit à en sortir. Elle se masque dans Réglages > Bandeau gauche, comme les autres vues.

### 0.4.15
- **Mise à jour des médias jamais importés** (Réglages > Métadonnées) : un bouton lance la mise à jour via TMDb des seuls titres sans fiche TMDb — scannés ou ajoutés à la main. Les réglages se ferment et la mise à jour démarre, avec sa barre de progression.

### 0.4.14
- **Sélection multiple** : le bandeau droit affiche le nombre de titres sélectionnés et leur liste ; chaque affiche sélectionnée porte une croix dans un rond (coin haut droit), qui retire le titre de la sélection d'un clic.
- **Barres de progression** : la mise à jour via TMDb, la relecture des infos techniques et l'import d'un dossier affichent une barre de progression avec le détail de l'étape en cours.

### 0.4.13
- **Purger le catalogue** (Réglages > Maintenance) : vide entièrement le catalogue ouvert (titres, exemplaires, personnes, collections, prêts, images) après deux confirmations ; ses réglages restent. La base est sauvegardée juste avant, les images ne le sont pas.

### 0.4.12
- **Personnes dans le bandeau droit** : dans la galerie des acteurs ou des réalisateurs, un clic affiche la personne dans le bandeau droit (photo, dates, biographie, liens TMDb et IMDb, ses films) ; un double-clic ouvre toujours sa fiche complète.

### 0.4.11
- **Acteurs et réalisateurs dans la zone centrale** : les liens du bandeau gauche affichent les personnes à la place des affiches, avec une recherche en haut ; les filtres de la Bibliothèque ramènent les affiches. Choisir un film dans la fiche d'une personne revient aux affiches sur ce film.

### 0.4.10
- **Icône des acteurs** : un petit personnage remplace le « i » dans la barre latérale, la distribution et les fiches de personnes, dans tous les jeux d'icônes (Buddha compris).

### 0.4.9
- **Cinedex sur Android** : application de consultation (fichier .apk) qui importe une sauvegarde faite sur l'ordinateur ; voir la rubrique « Cinedex sur Android ».

### 0.4.8
- Première version publiée sur GitHub : programmes d'installation pour Windows, Linux et macOS, et archives portables.

### 0.4.7
- Vue affiches : les affiches s'agrandissent pour occuper toute la largeur de la grille, sans vide à droite, en gardant leur format.

### 0.4.6
- **Fiche du bandeau droit redimensionnable** : glissez la poignée entre la liste et la fiche (de 300 à 760 px) ; la largeur est mémorisée pour le catalogue, un double-clic sur la poignée revient à la largeur par défaut. La grille d'affiches ajuste son nombre d'affiches par rangée à la place disponible.
- Le lien « Liste de souhaits » est retiré du bandeau gauche.

### 0.4.5
- Fiche du bandeau droit : les **genres** du titre sélectionné s'affichent en pastilles, entre les boutons et les notes.

### 0.4.4
- **Jetons de design** : toutes les marges, espacements, rayons, tailles de texte et couleurs d'exception viennent d'un seul dictionnaire, sur une grille de 4 px. Les espacements irréguliers sont alignés et les dernières ombres décoratives retirées. Un test vérifie à chaque compilation qu'aucune valeur en dur ne revient.

### 0.4.3
- **Nouvelle direction visuelle** : sol de pierre neutre, encre presque noire et un seul accent rouge velours réservé aux données ; menu et listes sélectionnés en pilule grise ; un seul bouton principal par écran, presque noir ; pastilles pâles ; échelle de 5 tailles de texte et 3 rayons d'angle dans toute l'application. Les préréglages d'apparence deviennent « Pierre » (menus) et « Rouge velours » / « Ambre » (accent).

### 0.4.2
- Ajout depuis le disque : case **Prendre en compte le fichier nfo si existant** (cochée par défaut). Décochée, les fichiers .nfo à côté des vidéos sont ignorés : titre et année viennent du nom du fichier.

### 0.4.1
- **Réalisateurs** : fiche avec biographie et films réalisés, ouverte d'un clic depuis la fiche d'un film (onglet Distribution) ou le panneau de droite ; annuaire des réalisateurs dans le bandeau gauche, affichable ou non dans les réglages.
- Le lien « Possédés » est retiré du bandeau gauche.

### 0.4.0
- **Grands catalogues** : liste et affiches virtualisées (seuls les éléments visibles existent), affiches chargées en arrière-plan, mises à jour ciblées ; testé sur 12 000 films.
- **Installation et mises à jour automatiques** (Cinedex-win-Setup.exe), versions Linux et macOS.
- **Suivi des séries** : épisodes à regarder, bouton Vu, calendrier des prochaines diffusions.
- **Accessibilité** : noms lus par les lecteurs d'écran pour tous les boutons, contrastes vérifiés, vue affiches au clavier.

### 0.3.1
- Onglet **Réseau** dans les Réglages : proxy HTTP ou HTTPS (serveur:port), désactivé par défaut, avec test de connexion.

### 0.3.0
- Onglet **Sauvegardes** : sauvegardes complètes (base, images, préférences au choix), à la demande ou planifiées, rotation et restauration.
- Onglet **Export** : liste des films en CSV, TSV ou Excel, avec choix des colonnes.
- Onglet **Liste des films** : choix des colonnes affichées dans la vue liste.
- Onglet **Apparence** : quatre jeux d'icônes cohérents, couleur des menus, couleur d'accent et police.

### 0.2.6
- Onglet **Maintenance** dans les Réglages : renommage du catalogue ouvert, sauvegarde, acteurs inutilisés, doublons d'acteurs et de réalisateurs, films en double, renumérotation selon le tri actuel, collections, tags et groupes vides, images inutilisées, contrôle d'intégrité et compactage.

### 0.2.5
- **Économiseur d'écran** : diaporama plein écran des affiches après 5 minutes d'inactivité, quand la fenêtre de Cinedex est active. Activation, durée d'inactivité, intervalle et titres se règlent dans Réglages > Économiseur d'écran.

### 0.2.4
- Gestion des **collections** (sagas TMDb) : présentation, affiche et décor téléchargés depuis TMDb, films possédés et films manquants de chaque saga, lien **Collections** dans le bandeau gauche. À activer ou désactiver dans Réglages > Général.
- Connexion à TMDb plus fiable sur les réseaux où IPv6 est annoncé mais ne fonctionne pas.

### 0.2.3
- Onglet **Statistiques** dans les Réglages : chiffres clés du catalogue et répartitions par genre, décennie, définition, support et disque.

### 0.2.2
- Journal des actions : chaque action faite dans Cinedex est notée (activé par défaut), avec export du journal du jour (Réglages > Général).

### 0.2.1
- Infobulle au survol d'un film de la liste : titre, genres, note IMDb et synopsis (à activer dans Réglages > Général).

### 0.2.0
- Nouveau nom : Bobine devient **Cinedex**, avec une nouvelle icône. Les données, catalogues et clés sont repris automatiquement.
- Interface en 10 langues, choisies dans les Réglages ; arabe de droite à gauche.
- Aide intégrée (F1).
- Fenêtre **À vérifier** : plus de fiche TMDb devinée ; les identifiants connus sont traités d'abord, les choix proposés à la fin, avec liens vers TMDb.
- **MediaID** : nom du disque de chaque fichier, disques USB reconnus quelle que soit leur lettre.
- **Recherche multicritère** sur tous les champs, combinée par ET ou OU.
- Fiche complète : notes, distribution cliquable, fichier et infos MediaInfo, parties CD1/CD2, gestion du prêt.
- Réglages par onglets ; bandeau gauche personnalisable avec sous-menu Définition ; mode sombre ; fenêtre agrandie au démarrage et F11.
- Durées en heures et minutes ; notes IMDb, Rotten Tomatoes et Metacritic.
- Plus rapide sur les grands catalogues (index de base de données, recherche à la frappe).

### 0.1.0
- Première version : catalogue, import de dossiers, fiches TMDb, séries, prêts, plusieurs catalogues, fichiers .nfo.
