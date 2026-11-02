"""Extrait tous les textes français affichables de Cinedex vers src/Cinedex.Core/Localization/Lang/catalog.json.

Le texte français sert de clé de traduction : après avoir ajouté ou modifié des textes dans l'interface,
relancer ce script (python build/i18n-extract.py), puis ajouter les traductions dans Lang/<code>.json.
Le test LocalizationTests signale les textes des fenêtres absents du catalogue.
"""
import os, re, json, html

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
    if re.search(r'\b(SELECT|INSERT|UPDATE|DELETE|CREATE|WHERE|JOIN|PRAGMA|EXISTS|coalesce|ORDER BY|IS NOT NULL|AND|OR|NOT)\b|@\w|\w\.\w+ =|<>', s):
        return False
    if re.fullmatch(r'[a-z0-9_]+', s) or s in ('Bobine', 'Bearer', 'Application Support', 'Cinedex'):
        return False
    return True


# 1. XAML
ATTR = re.compile(r'\b(Text|Content|Header|Title|PlaceholderText|Watermark|ToolTip\.Tip)="([^"{][^"]*)"')
PARAM = re.compile(r"ConverterParameter='([^']*)'")
for dirpath, _, files in os.walk(os.path.join(ROOT, 'Cinedex.App')):
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
SKIP_LINE = re.compile(r'^\s*(//|///|\[)|Log(Information|Warning|Error|Debug|Trace)|GeneratedRegex|Regex\.|IsMatch\(|Tool\.Run|GetManifestResource|DefaultRequestHeaders|nameof\(')
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
