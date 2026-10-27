# StatusLookout

### Sons des notifications

Dans **Préférences → Notifications → Personnaliser les sons des notifications**, choisis un son distinct pour les pannes, les dégradations, les retours à la normale, les maintenances et les mises à jour. Tu peux utiliser le son Windows, le silence, quatre sons intégrés ou un fichier WAV. **Écouter** permet de tester ton choix. Le volume de l’application concerne les sons intégrés et importés ; le son Windows suit le volume de Windows.

Les fichiers importés doivent être des WAV PCM standard de 16 bits, de 10 secondes et 2 Mo maximum. Ils sont copiés dans `%LOCALAPPDATA%\StatusLookout\Sounds`. Les fichiers personnalisés ne sont pas inclus dans les exports de préférences : réimporte-les sur un autre PC. Les alertes restent silencieuses quand le son général est désactivé ; le son natif des notifications est coupé lors de la lecture d’un son personnalisé pour éviter un doublon.

**Tes services. Un signal clair.**

StatusLookout est une application Windows native qui surveille les pages de statut de services cloud depuis la zone de notification.

![Tableau de bord en français, thème clair](../assets/screenshot-light-fr.png)

*Capture de l'application en mode démonstration, avec incidents simulés.*

## Téléchargements par mois

![Téléchargements mensuels des exécutables Windows par architecture](../assets/downloads-monthly.svg)

Le graphique et le badge sont actualisés automatiquement chaque jour et après les publications Windows. Ils comptent les téléchargements des `.exe` des versions stables, y compris les premières versions UptimeKite, et non les utilisateurs uniques. Le suivi mensuel commence en octobre 2026 : GitHub fournit des compteurs cumulés, sans historique daté des téléchargements. Les mois antérieurs ne sont donc pas reconstitués. Consulte la [méthode de comptage et les données publiques](metrics/README.md).

## Télécharger

Choisis ton architecture dans la [dernière version](https://github.com/Bouneh/statuslookout/releases/latest) :

- **x64** : la plupart des PC Intel et AMD.
- **ARM64** : PC Windows ARM / Snapdragon.
- **x86** : Windows 32 bits.

Télécharge **StatusLookout-Setup-win-x64.exe**, **StatusLookout-Setup-win-arm64.exe** ou **StatusLookout-Setup-win-x86.exe**. L’installateur inclut .NET. Il fonctionne sur Windows 10 22H2 et Windows 11, sans droits administrateur. Les versions ne sont pas encore signées numériquement.

## Utilisation

1. Quitte l’ancienne application depuis sa zone de notification, puis lance l’installateur. Il installe StatusLookout dans `%LOCALAPPDATA%\Programs\StatusLookout`, ajoute un raccourci au menu Démarrer et propose le raccourci Bureau ainsi que le lancement avec Windows. Les versions portables restent disponibles dans la release.
2. L’anglais est la langue par défaut. Ouvre **Preferences → Appearance & language**, puis sélectionne **Français**. Le changement est immédiat et mémorisé.
3. Dans **Préférences → Apparence et langue**, choisis le thème **Clair** ou **Sombre**.
4. Ouvre le **Catalogue de services**, recherche et coche les services à surveiller.
5. Dans **Préférences**, sélectionne les alertes et utilise **Envoyer une alerte de test**.
6. Active ou désactive **Lancer à l’ouverture de session Windows** selon ton choix.
7. Ferme la fenêtre pour continuer la surveillance. Utilise **Quitter StatusLookout** dans la zone de notification pour l’arrêter.

Les titres des alertes et le menu de la zone de notification suivent la langue sélectionnée. Les descriptions d’incident restent dans la langue publiée par les fournisseurs.

Le catalogue contient **1107 services et produits**, dont OVHcloud (six domaines regroupés), Hetzner, Supabase, Render, Mistral AI, Groq, Hugging Face, Resend, Tailscale et Streamlabs. Tu peux ajouter une page de statut personnalisée compatible Statuspage ou Instatus.

Les vérifications ont lieu toutes les 60 secondes par défaut. L'intervalle peut varier de 30 à 900 secondes. Une erreur réseau produit un état **Indisponible**, sans être confondue avec une panne confirmée.

L'historique des 300 derniers changements et les préférences restent dans `%LOCALAPPDATA%\StatusLookout`. Les états confirmés sont conservés entre les démarrages pour éviter les alertes répétées et détecter les rétablissements.

Les alertes dépendent des informations publiées par les fournisseurs et des réglages de notifications Windows. La surveillance fonctionne tant que l'application tourne et que le PC est connecté et éveillé.

## À propos et mises à jour

La page **À propos** affiche la version installée, **LAMOUREUX Fabien**, son [profil GitHub](https://github.com/Bouneh), le dépôt et la licence MIT.

Clique sur **Vérifier les mises à jour**, puis sur **Télécharger** pour récupérer l’installateur adapté au PC. Le téléchargement peut être annulé et son empreinte SHA-256 est vérifiée.

Le bouton **Installer la mise à jour** demande confirmation, vérifie à nouveau le fichier, ouvre l’installateur et ferme l’application. L’installation existante est mise à jour et toutes les listes, préférences, sons et l’historique sont conservés. Aucun remplacement silencieux n’est effectué.

Pour migrer une première fois depuis une version antérieure à 0.8.0, télécharge l’installateur depuis GitHub : ces anciennes versions ne connaissent que les exécutables portables.

Pour désinstaller, quitte l’application puis utilise **Paramètres Windows → Applications → Applications installées → StatusLookout → Désinstaller**. Les données restent dans `%LOCALAPPDATA%\StatusLookout` pour une éventuelle réinstallation.

![À propos et mises à jour](../assets/about-fr-light.png)

Les **1107 entrées** du catalogue affichent désormais un logo intégré. Les produits Google Cloud et Firebase ainsi que les régions Sendbird partagent le logo de leur plateforme. Les logos intégrés ne nécessitent pas de requête réseau supplémentaire. Les icônes des services personnalisés sont recherchées sur leur page de statut et le serveur qui héberge l’icône.

## Alertes et composants

- **Regrouper les alertes du même incident**, activé par défaut dans les préférences : une seule notification par vérification pour les services du même fournisseur qui partagent les mêmes identifiants d’incident et le même type d’alerte. L’historique reste individuel.
- **Options du service → Suspendre les alertes** : 30 minutes ou 1 heure. La surveillance et l’historique continuent, même après redémarrage. Les alertes suspendues ne sont pas rejouées ensuite ; la coupure permanente reste indépendante.
- **Options du service → Choisir les composants** : API, régions ou produits exposés par Statuspage / Instatus, produits Google du catalogue, systèmes Hetzner, ressources Hugging Face ou composants Streamlabs. Sans sélection, toute la page est surveillée ; **Choix par défaut** restaure le périmètre initial.
- **Préférences → Apparence et langue → Suivre le thème Windows** : le thème suit automatiquement celui des applications Windows, y compris pendant l’utilisation.
- **Préférences → Données locales → Exporter / Importer les préférences** : transfère la liste et les services personnalisés, les composants et les réglages. L’import remplace les préférences actuelles : exporte d’abord pour conserver une sauvegarde. Le démarrage Windows, l’historique, les logos et les suspensions temporaires ne sont pas transférés.

Les services personnalisés récupèrent maintenant l’icône publiée par leur page de statut HTTPS, notamment Mailjet, et la conservent en cache pendant 30 jours. Les icônes intégrées sont réutilisées lorsqu’elles correspondent. À défaut d’icône lisible, les initiales restent affichées.

## Plugin Stream Deck

Le plugin est inclus dans le [même dépôt](../plugins/streamdeck/README.md) et les [versions GitHub](https://github.com/Bouneh/statuslookout/releases/latest). Il nécessite **StatusLookout 0.5.0+** lancé sur le PC et **Stream Deck 7.1+** sous le même compte Windows.

Installe `StatusLookout-streamdeck-0.2.0.streamDeckPlugin`, puis ajoute une action de la catégorie **StatusLookout** sur une touche libre :

- **Statut d’un service** : logo, nom et couleur ; un appui ouvre sa page de statut.
- **État global** : compteurs des services suivis, touchés et indisponibles ; un appui ouvre l’application.
- **Actualiser les services** : lance une vérification.
- **Suspendre les alertes d’un service** : 30 minutes ou 1 heure ; un nouvel appui termine la suspension.

Le catalogue complet et les services personnalisés, dont Mailjet, sont disponibles avec une recherche. Choisir un service sur une touche active sa surveillance. AWS comprend une vue globale, 267 produits et 36 régions ; les filtres de produit et de région peuvent être combinés. Seuls les événements publics AWS sont suivis, sans compte ni identifiants, et les incidents actifs sont affichés comme dégradés sans déduire une gravité non définie par ce flux. Les réglages des touches proposent l’anglais, le français et la langue de StatusLookout. La liaison locale est réservée au compte Windows courant, sans serveur HTTP. Application fermée, les touches affichent **Déconnecté**, puis se reconnectent automatiquement. Les touches avec écran sont prises en charge ; les molettes et pédales ne le sont pas.

## Développement

Installe le SDK .NET 10 sur Windows :

```powershell
dotnet build StatusLookout.sln -c Release
dotnet run --project tests/StatusLookout.Tests -c Release
.\scripts\Publish.ps1
```

GitHub Actions construit et publie les exécutables x64, x86 et ARM64 lors d'un tag `v*`. Le code est sous licence MIT. Voir le [README complet](../README.md) et les [instructions de contribution](../CONTRIBUTING.md).

## Catalogue et familles de services

Le catalogue contient **1107 services** et couvre les **1048 entrées** du catalogue stts épinglé à `876ecac0996c719786477af8e3833cdac05235ba`. La correspondance est enregistrée dans `src/StatusLookout.Core/Catalog/stts-coverage.json`.

Google, AWS, Apple, Adobe, Azure et Salesforce sont regroupés en familles repliables. Coche la famille pour sélectionner tous ses services, ou déplie-la pour choisir certains produits. Une case partielle signale une sélection incomplète. La sélection du groupe inclut ses services masqués par la recherche. Les boutons **Tout déplier / Tout replier** permettent de parcourir le catalogue. Les choix sont sauvegardés et exportables. Le sélecteur Stream Deck propose aussi des familles repliables.

Certains fournisseurs peuvent bloquer les requêtes ou retirer une ancienne API. Ces services restent dans le catalogue mais apparaissent **Indisponibles**, sans inventer un état opérationnel ou une panne.

## Listes de surveillance

Le sélecteur **Liste** propose **Travail** et **Personnel**. Tes services actuels sont conservés dans Travail ; Personnel commence vide. **Nouvelle liste** crée une liste personnalisée vide à remplir depuis le catalogue. Les listes personnalisées peuvent être renommées ou supprimées. Seule la liste active est surveillée et utilisée par Stream Deck. Les services personnalisés, filtres de composants et réglages d’alerte sont partagés. Toutes les listes et la dernière liste active sont sauvegardées et incluses dans l’import/export.

## Vérification automatique des mises à jour

Activée par défaut, elle vérifie GitHub 30 secondes après le lancement puis toutes les 24 heures pendant que l’application tourne. En cas d’échec, elle réessaie après une heure. Une notification est envoyée une seule fois par nouvelle version, avec un bouton **Télécharger la mise à jour**. Un bandeau ouvre aussi les contrôles de téléchargement. L’option se désactive dans **À propos**. Le téléchargement est lancé sur demande et son SHA-256 est vérifié ; le remplacement de l’exécutable reste manuel.
