# StatusLookout

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

Le fichier `.exe` est autonome, avec .NET inclus. Il fonctionne sur Windows 10 22H2 et Windows 11, sans droits administrateur. Les versions ne sont pas encore signées numériquement.

## Utilisation

1. Place le fichier dans un dossier permanent et lance-le.
2. L’anglais est la langue par défaut. Ouvre **Preferences → Appearance & language**, puis sélectionne **Français**. Le changement est immédiat et mémorisé.
3. Dans **Préférences → Apparence et langue**, choisis le thème **Clair** ou **Sombre**.
4. Ouvre le **Catalogue de services**, recherche et coche les services à surveiller.
5. Dans **Préférences**, sélectionne les alertes et utilise **Envoyer une alerte de test**.
6. Active ou désactive **Lancer à l’ouverture de session Windows** selon ton choix.
7. Ferme la fenêtre pour continuer la surveillance. Utilise **Quitter StatusLookout** dans la zone de notification pour l’arrêter.

Les titres des alertes et le menu de la zone de notification suivent la langue sélectionnée. Les descriptions d’incident restent dans la langue publiée par les fournisseurs.

Le catalogue contient **461 services et produits**. Tu peux ajouter une page de statut personnalisée compatible Statuspage ou Instatus.

Les vérifications ont lieu toutes les 60 secondes par défaut. L'intervalle peut varier de 30 à 900 secondes. Une erreur réseau produit un état **Indisponible**, sans être confondue avec une panne confirmée.

L'historique des 300 derniers changements et les préférences restent dans `%LOCALAPPDATA%\StatusLookout`. Les états confirmés sont conservés entre les démarrages pour éviter les alertes répétées et détecter les rétablissements.

Les alertes dépendent des informations publiées par les fournisseurs et des réglages de notifications Windows. La surveillance fonctionne tant que l'application tourne et que le PC est connecté et éveillé.

## À propos et mises à jour

La page **À propos** affiche la version installée, **LAMOUREUX Fabien**, son [profil GitHub](https://github.com/Bouneh), le dépôt et la licence MIT.

Clique sur **Vérifier les mises à jour**. Si une version stable plus récente existe, un bouton permet de télécharger l’exécutable adapté au PC. La progression est affichée et tu peux annuler. Son empreinte SHA-256 est vérifiée avant l’enregistrement dans **Téléchargements** ; les fichiers incomplets sont supprimés.

Ouvre le dossier du téléchargement, quitte l’application puis remplace l’ancien exécutable. Conserve le même chemin pour le démarrage automatique, ou désactive et réactive cette option après déplacement. Les préférences et l’historique restent conservés. Le téléchargement ne remplace pas automatiquement l’application ouverte.

![À propos et mises à jour](../assets/about-fr-light.png)

Les **461 entrées** du catalogue affichent désormais un logo intégré. Les produits Google Cloud et Firebase ainsi que les régions Sendbird partagent le logo de leur plateforme. Les services personnalisés sans logo connu affichent leurs initiales. L’affichage des logos fonctionne sans requête réseau supplémentaire.

## Idées pour la suite

Ces fonctionnalités sont proposées et **ne sont pas encore implémentées** :

- **Profils de services** : travail, personnel et développement.
- **Alertes regroupées** : une notification pour plusieurs services touchés par le même incident.
- **Installation des mises à jour en un clic**, avec vérification et conservation des préférences.
- **Pause temporaire des alertes** et **import/export de la liste des services**.

La [feuille de route](../ROADMAP.md) contient les priorités et d’autres idées. Les suggestions sont les bienvenues dans les [issues GitHub](https://github.com/Bouneh/statuslookout/issues).

## Développement

Installe le SDK .NET 10 sur Windows :

```powershell
dotnet build StatusLookout.sln -c Release
dotnet run --project tests/StatusLookout.Tests -c Release
.\scripts\Publish.ps1
```

GitHub Actions construit et publie les exécutables x64, x86 et ARM64 lors d'un tag `v*`. Le code est sous licence MIT. Voir le [README complet](../README.md) et les [instructions de contribution](../CONTRIBUTING.md).
