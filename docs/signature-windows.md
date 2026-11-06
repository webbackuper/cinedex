# Signer les paquets Windows avec la SignPath Foundation

Sans signature, Windows affiche l'avertissement **SmartScreen** (« Windows a
protégé votre ordinateur ») à quiconque télécharge le programme d'installation.
La [SignPath Foundation](https://signpath.org/) signe gratuitement les projets
libres, ce qui le fait disparaître.

Tout ce qui pouvait être préparé dans le dépôt l'est déjà :

| Prérequis de la Fondation | État |
| --- | --- |
| Licence approuvée par l'OSI | `LICENSE` (MIT) |
| Code source public | <https://github.com/webbackuper/cinedex> |
| Construction par intégration continue, sans intervention manuelle | `.github/workflows/release.yml` |
| Politique de signature publiée | `CODE_SIGNING_POLICY.md` |
| Authentification à plusieurs facteurs | à activer sur GitHub et SignPath |

## Ce qu'il reste à faire, et que seul le propriétaire du dépôt peut faire

1. **Demander l'adhésion** sur <https://signpath.org/apply>, au nom du projet
   Cinedex, avec le lien du dépôt. La Fondation vérifie la licence et le
   caractère public des constructions.
2. **Créer le projet** dans l'organisation SignPath qui vous est attribuée, avec
   une *signing policy* (en général `release-signing`) et une *artifact
   configuration* décrivant ce qu'il faut signer dans l'archive :
   `Cinedex-win-Setup.exe`, et `Cinedex.exe` à l'intérieur des paquets
   `.nupkg`.
3. **Déclarer quatre variables et un secret** dans les réglages GitHub du dépôt
   (*Settings → Secrets and variables → Actions*) :

   | Nom | Type | Valeur |
   | --- | --- | --- |
   | `SIGNPATH_ORGANIZATION_ID` | variable | l'identifiant de l'organisation SignPath |
   | `SIGNPATH_PROJECT_SLUG` | variable | le nom court du projet, par exemple `cinedex` |
   | `SIGNPATH_POLICY_SLUG` | variable | le nom court de la politique, par exemple `release-signing` |
   | `SIGNPATH_ARTIFACT_SLUG` | variable | le nom court de la configuration d'artefact |
   | `SIGNPATH_API_TOKEN` | secret | le jeton d'API de l'utilisateur CI |

4. **Connecter l'application GitHub de SignPath** au dépôt : c'est elle qui
   permet à SignPath de vérifier que l'artefact vient bien d'une construction
   de ce dépôt, et non d'un poste quelconque.

## Comment le workflow se comporte

Le travail `signature` de `.github/workflows/release.yml` ne démarre que si
`SIGNPATH_ORGANIZATION_ID` est renseignée. Tant qu'elle ne l'est pas, il est
sauté et la publication reprend les paquets non signés, exactement comme
aujourd'hui. Une fois les variables en place, il reprend l'artefact Windows, le
soumet à la signature, attend le résultat et remplace l'artefact par sa version
signée, que la publication envoie ensuite sur la *release*.

Un échec de la signature arrête la publication : mieux vaut pas de version du
tout qu'une version dont on croit à tort qu'elle est signée.

## Ce que verront vos utilisateurs

L'éditeur affiché par Windows sera **SignPath Foundation**, et non votre nom :
le certificat appartient à la Fondation, qui le prête à tous les projets
libres qu'elle signe. Si vous préférez votre propre nom, il faut un certificat
**EV** acheté à une autorité (de l'ordre de 150 à 400 € par an, avec un jeton
matériel ou un HSM en ligne) ; la signature se fait alors autrement, et le
travail `signature` doit être remplacé par l'outil du fournisseur.

## Et sans signature ?

Chaque archive publiée est accompagnée de son empreinte **SHA-256**. Un
utilisateur méfiant peut la vérifier :

```powershell
Get-FileHash .\Cinedex-win-Setup.exe -Algorithm SHA256
```

et comparer avec le fichier `.sha256` de la *release*.
