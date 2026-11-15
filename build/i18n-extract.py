"""Extrait tous les textes français affichables de Cinedex vers src/Cinedex.Core/Localization/Lang/catalog.json.

Le texte français sert de clé de traduction : après avoir ajouté ou modifié des textes dans l'interface,
relancer ce script (python build/i18n-extract.py), puis ajouter les traductions dans Lang/<code>.json.
Le test LocalizationTests signale les textes des fenêtres absents du catalogue.

Options, toutes appliquées après l'extraction :
  --missing      liste, langue par langue, les textes du catalogue qui n'ont pas de traduction ;
  --prune        retire des dictionnaires les textes que le catalogue ne contient plus ;
  --add FICHIER  verse dans les dictionnaires les traductions d'un fichier JSON {"<code>": {"<texte>": "<traduction>"}}.
Les fichiers de langue ne sont écrits qu'ici, pour que leur format n'existe qu'à un seul endroit.
"""
import argparse, io, os, re, json, html

parser = argparse.ArgumentParser(description="Catalogue des textes de Cinedex et dictionnaires de langue.")
parser.add_argument('--missing', action='store_true', help="liste les textes sans traduction, par langue")
parser.add_argument('--prune', action='store_true', help="retire des dictionnaires les textes absents du catalogue")
parser.add_argument('--add', metavar='FICHIER', help="verse les traductions d'un fichier JSON : code de langue, puis texte et traduction")
args = parser.parse_args()

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'src')
OUT = os.path.join(ROOT, 'Cinedex.Core', 'Localization', 'Lang', 'catalog.json')
strings = set()
SKIP_DIR = re.compile(r'[\\/](obj|bin)([\\/]|$)')

FRENCH = re.compile(r'[A-Za-zÀ-ÿ]{2,}')


def keep(s):
    s = s.strip('\n')
    if not s or not FRENCH.search(s):
        return False
    if re.fullmatch(r'[\w.\-/]+\.(nfo|json|db|mkv|jpg|png|ico|exe|bak|tmp|log|sql|xml)', s):
        return False
    if re.fullmatch(r'[a-z_]+(\.[a-zA-Z_]+)+', s):  # clés de réglages
        return False
    if re.match(r'^(https?://|/|\.|#|\{(?!\d)|\^|\(\?|tt\d|movie/|tv/)', s):
        return False
    if re.search(r'\b(SELECT|INSERT|UPDATE|DELETE|CREATE|WHERE|JOIN|PRAGMA|EXISTS|coalesce|ORDER BY|IS (NOT )?NULL|AND|OR|NOT|CASE|substr)\b|@\w|\w\.\w+ =|<>', s):
        return False
    if re.fullmatch(r'[a-z0-9_]+', s) or s in ('Bobine', 'Bearer', 'Application Support', 'Cinedex'):
        return False
    return True


# 1. XAML
ATTR = re.compile(r'\b(Text|Content|Header|Title|PlaceholderText|Watermark|ToolTip\.Tip)="([^"{][^"]*)"')
PARAM = re.compile(r"ConverterParameter='([^']*)'")
for dirpath, _, files in [w for app in ('Cinedex.App', 'Cinedex.Android') for w in os.walk(os.path.join(ROOT, app))]:
    if SKIP_DIR.search(dirpath):
        continue
    for f in files:
        if f.endswith('.axaml'):
            src = open(os.path.join(dirpath, f), encoding='utf-8-sig').read()
            src = re.sub(r'<!--.*?-->', '', src, flags=re.S)
            for m in ATTR.finditer(src):
                v = html.unescape(m.group(2))
                if keep(v):
                    strings.add(v)
            for m in PARAM.finditer(src):
                v = m.group(1)
                v = v[2:] if v.startswith('{}') else v
                if keep(v):
                    strings.add(v)

# 2. C# : littéraux (y compris Tr.T / Tr.F), hors commentaires, journaux, SQL
LIT = re.compile(r'(?<![@$\w])"((?:[^"\\\n]|\\.)*)"')
SKIP_LINE = re.compile(r'^\s*(//|///|\[)|Log(Information|Warning|Error|Debug|Trace)|GeneratedRegex|Regex\.|IsMatch\(|Tool\.Run|GetManifestResource|DefaultRequestHeaders|nameof\(|Journal\(|Write\(\$?"')
# (Journal( et .Write($"…") : le journal des actions est écrit en français, hors traduction.)
for dirpath, _, files in os.walk(ROOT):
    if SKIP_DIR.search(dirpath) or 'Cinedex.Media' in dirpath or 'Cinedex.Metadata' in dirpath:
        continue
    for f in files:
        if not f.endswith('.cs') or f in ('Tr.cs', 'FileNameParser.cs', 'MediaProbe.cs', 'NfoReader.cs', 'NfoWriter.cs', 'TmdbDtos.cs', 'Program.cs'):
            continue
        path = os.path.join(dirpath, f)
        text = open(path, encoding='utf-8-sig').read()
        text = re.sub(r'"""(.*?)"""', '""', text, flags=re.S)  # chaînes brutes (SQL)
        for line in text.split('\n'):
            if SKIP_LINE.search(line):
                continue
            for m in LIT.finditer(line):
                raw = m.group(1)
                try:
                    v = json.loads('"' + raw.replace('\\\'', "'") + '"')
                except json.JSONDecodeError:
                    v = raw
                if keep(v) and (' ' in v.strip() or re.search('[À-ÿ]', v) or re.fullmatch(r'[A-ZÀ-Ý][a-zà-ÿ]+', v)):
                    strings.add(v)

# 3. Données de référence (genres, supports, boîtiers, versions)
ref = open(os.path.join(ROOT, 'Cinedex.Data', 'Migrations', '0002_reference_data.sql'), encoding='utf-8-sig').read()
for m in re.finditer(r"\('([^']*)',\s*'((?:[^']|'')*)'(?:,\s*'((?:[^']|'')*)')?", ref):
    v = (m.group(3) if m.group(3) is not None else m.group(2)).replace("''", "'")
    if keep(v):
        strings.add(v)

result = sorted(strings, key=lambda s: s.lower())
os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(result, open(OUT, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(result), 'textes')

# 4. Dictionnaires de langue : traductions à verser, textes orphelins, textes manquants.
LANG_DIR = os.path.dirname(OUT)
catalog = set(result)


def languages():
    return sorted(f[:-5] for f in os.listdir(LANG_DIR) if f.endswith('.json') and f != 'catalog.json')


def read(code):
    with io.open(os.path.join(LANG_DIR, code + '.json'), encoding='utf-8') as f:
        return json.load(f)


def write(code, data):
    """Un texte par ligne, clés triées, une espace d'indentation, fins de ligne CRLF."""
    lines = ['{']
    keys = sorted(data)
    for i, key in enumerate(keys):
        comma = '' if i == len(keys) - 1 else ','
        lines.append(' %s: %s%s' % (json.dumps(key, ensure_ascii=False), json.dumps(data[key], ensure_ascii=False), comma))
    lines.append('}')
    with io.open(os.path.join(LANG_DIR, code + '.json'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('\n'.join(lines) + '\n')


if args.add:
    with io.open(args.add, encoding='utf-8') as f:
        added = json.load(f)
    for code, texts in sorted(added.items()):
        unknown = [k for k in texts if k not in catalog]
        if unknown:
            raise SystemExit('%s : %d texte(s) absents du catalogue, par exemple %r' % (code, len(unknown), unknown[0]))
        data = read(code)
        data.update(texts)
        write(code, data)
        print('%s : %d traduction(s) versée(s)' % (code, len(texts)))

if args.prune:
    for code in languages():
        data = read(code)
        orphans = [k for k in data if k not in catalog]
        if orphans:
            for k in orphans:
                del data[k]
            write(code, data)
        print('%s : %d texte(s) orphelin(s) retiré(s)' % (code, len(orphans)))

if args.missing:
    reference = None
    for code in languages():
        data = read(code)
        missing = [k for k in result if k not in data]
        print('%s : %d texte(s) sans traduction' % (code, len(missing)))
        reference = missing if reference is None or len(missing) < len(reference) else reference
    # Les textes qu'aucune langue ne traduit sont le plus souvent des noms propres ; ceux qui manquent
    # à une langue et pas aux autres sont les oublis.
    everywhere = set(reference or [])
    for code in languages():
        data = read(code)
        forgotten = [k for k in result if k not in data and k not in everywhere]
        for k in forgotten:
            print('  %s : %r' % (code, k))
