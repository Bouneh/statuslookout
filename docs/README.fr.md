# UptimeKite

**Tes services. Un signal clair.**

UptimeKite est une application Windows native qui surveille les pages de statut de services cloud depuis la zone de notification.

![Tableau de bord](../assets/screenshot.png)

*Capture de l'application en mode démonstration, avec incidents simulés.*

## Télécharger

Choisis ton architecture dans la [dernière version](https://github.com/Bouneh/uptimekite/releases/latest) :

- **x64** : la plupart des PC Intel et AMD.
- **ARM64** : PC Windows ARM / Snapdragon.
- **x86** : Windows 32 bits.

Le fichier `.exe` est autonome, avec .NET inclus. Il fonctionne sur Windows 10 22H2 et Windows 11, sans droits administrateur. Les versions ne sont pas encore signées numériquement.

## Utilisation

1. Place le fichier dans un dossier permanent et lance-le.
2. Ouvre **Service library** : recherche et coche les services à surveiller.
3. Dans **Preferences**, active les alertes de panne, de dégradation et de retour à la normale.
4. Utilise **Send test alert** pour vérifier les notifications Windows.
5. Active **Launch at Windows sign-in** pour démarrer automatiquement.
6. Ferme la fenêtre : la surveillance continue dans la zone de notification. Pour arrêter l'application, utilise **Quit UptimeKite** dans son menu.

Le catalogue contient **461 services et produits**. Tu peux ajouter une page de statut personnalisée compatible Statuspage ou Instatus.

Les vérifications ont lieu toutes les 60 secondes par défaut. L'intervalle peut varier de 30 à 900 secondes. Une erreur réseau produit un état **Unavailable**, sans être confondue avec une panne confirmée.

L'historique des 300 derniers changements et les préférences restent dans `%LOCALAPPDATA%\UptimeKite`. Les états confirmés sont conservés entre les démarrages pour éviter les alertes répétées et détecter les rétablissements.

Les alertes dépendent des informations publiées par les fournisseurs et des réglages de notifications Windows. La surveillance fonctionne tant que l'application tourne et que le PC est connecté et éveillé.

## Développement

Installe le SDK .NET 10 sur Windows :

```powershell
dotnet build UptimeKite.sln -c Release
dotnet run --project tests/UptimeKite.Tests -c Release
.\scripts\Publish.ps1
```

GitHub Actions construit et publie les exécutables x64, x86 et ARM64 lors d'un tag `v*`. Le code est sous licence MIT. Voir le [README complet](../README.md) et les [instructions de contribution](../CONTRIBUTING.md).
