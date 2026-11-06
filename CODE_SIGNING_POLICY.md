# Politique de signature du code

Les programmes d'installation et les exécutables Windows de Cinedex sont signés
gratuitement par [SignPath.io](https://signpath.io/), avec un certificat de la
[SignPath Foundation](https://signpath.org/).

> **État actuel** : la chaîne de publication est prête, la demande d'adhésion à la
> Fondation reste à déposer. Les versions publiées jusqu'ici ne sont donc pas
> signées ; elles portent chacune leur empreinte SHA-256.

Windows affiche donc **SignPath Foundation** comme éditeur, et non le nom de
l'auteur de Cinedex : c'est la Fondation qui porte le certificat, pour tous les
projets libres qu'elle signe.

## Ce qui est signé

| Fichier | Signé |
| --- | --- |
| `Cinedex-win-Setup.exe` | oui |
| `Cinedex.exe` à l'intérieur des paquets Windows | oui |
| Archives portables Linux et macOS | non : la signature Authenticode est propre à Windows |
| APK Android | signé séparément, avec la clé de l'auteur |

Toutes les publications portent par ailleurs une empreinte **SHA-256** dans un
fichier `.sha256` à côté de chaque archive : elle permet de vérifier un
téléchargement même sans signature.

## Qui peut déclencher une signature

Seules les constructions lancées par le dépôt
[webbackuper/cinedex](https://github.com/webbackuper/cinedex) sur une étiquette
de version (`vX.Y.Z`) peuvent demander une signature. La demande part du
workflow `Paquets`, à partir du code public du dépôt ; aucune signature n'est
possible depuis un poste de développement.

## Données personnelles

Cinedex ne transmet aucune donnée à SignPath. La signature porte sur des
fichiers de programme, pas sur les catalogues ni sur les données des
utilisateurs, qui restent sur leur machine.

## Rôles et accès

| Rôle | Qui |
| --- | --- |
| Propriétaire du dépôt et des publications | webbackuper |
| Autorité de certification | SignPath Foundation |
| Service de signature | SignPath.io |

L'accès au dépôt et au service de signature est protégé par une
authentification à plusieurs facteurs, comme les conditions de la SignPath
Foundation l'exigent.
