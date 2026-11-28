using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.App.Services;

/// <summary>
/// <c>Catalogue.html</c> d'un coffret : une page autonome (aucune ressource extérieure, aucun CDN) qui montre les
/// films du disque avec leurs affiches, synopsis, distribution et notes, avec recherche, tri et filtre par genre,
/// en clair et en sombre. Elle sert sur un Mac, ou quand l'exécutable ne peut pas être lancé.
/// <para>
/// Les métadonnées viennent d'Internet (TMDb) : elles ne sont jamais écrites comme du HTML. Les données
/// partent en JSON dans une balise de script (où <c>&lt;</c>, <c>&gt;</c> et <c>&amp;</c> sont échappés), et la page
/// les affiche avec <c>textContent</c> : un synopsis qui contiendrait une balise reste du texte.
/// </para>
/// </summary>
public static class GiftCatalogPage
{
    private static readonly JsonSerializerOptions Json = new()
    {
        // Encodeur par défaut : échappe <, >, &, ' et les caractères non ASCII sûrs pour une page HTML.
        WriteIndented = false,
    };

    /// <summary>Un film de la page.</summary>
    public sealed record Entry(
        string Title,
        string? OriginalTitle,
        int? Year,
        string? Runtime,
        string? Genres,
        string? Overview,
        string? Tagline,
        double? Imdb,
        double? Tmdb,
        string? Directors,
        string? Cast,
        string? Poster,
        IReadOnlyList<string> Files);

    public static string Build(
        string name,
        TitleRepository titles,
        PeopleRepository? people,
        IReadOnlyDictionary<long, string> relativePaths,
        MediaFileRepository files,
        ImageStore images,
        string root)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(images);
        var entries = new List<Entry>();
        foreach (var summary in titles.Query(new TitleQuery()))
        {
            var detail = titles.Get(summary.Id);
            var credits = people?.Credits(summary.Id) ?? [];
            var poster = images.PathFor(summary.Id, "poster") is { } path && File.Exists(path) ? Link(root, path) : null;
            entries.Add(new Entry(
                summary.Title,
                string.Equals(detail?.OriginalTitle, summary.Title, StringComparison.Ordinal) ? null : detail?.OriginalTitle,
                summary.Year,
                TitleText.FormatRuntime(summary.RuntimeMinutes),
                summary.GenresText.Length == 0 ? null : summary.GenresText,
                detail?.Overview,
                detail?.Tagline,
                detail?.ImdbRating,
                detail?.TmdbRating,
                Join(credits.Where(c => c.Role == "director").Select(c => c.Name).Distinct()),
                Join(credits.Where(c => c.Role == "actor").OrderBy(c => c.Order).Take(8).Select(c => c.Name)),
                poster,
                [.. files.FilesOf(summary.Id).Where(f => relativePaths.ContainsKey(f.Id)).Select(f => ToUrl(relativePaths[f.Id]))]));
        }

        return Render(name, entries);
    }

    /// <summary>La page, à partir de ses films : séparé pour être vérifié sans base.</summary>
    public static string Render(string name, IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string H(string text) => WebUtility.HtmlEncode(text);
        var labels = new Dictionary<string, string>
        {
            ["search"] = Tr.T("Rechercher un film…"),
            ["allGenres"] = Tr.T("Tous les genres"),
            ["sortTitle"] = Tr.T("Titre"),
            ["sortYear"] = Tr.T("Année"),
            ["sortImdb"] = Tr.T("Note IMDb"),
            ["open"] = Tr.T("Ouvrir le fichier"),
            ["folder"] = Tr.T("Ouvrir le dossier"),
            ["directors"] = Tr.T("Réalisation"),
            ["cast"] = Tr.T("Avec"),
            ["close"] = Tr.T("Fermer"),
            ["count"] = Tr.T("{0} film(s)"),
            ["noFile"] = Tr.T("Pas de fichier vidéo sur ce disque."),
        };
        var data = JsonSerializer.Serialize(new { labels, films = entries }, Json);
        var page = new StringBuilder();
        page.Append("<!DOCTYPE html>\n<html lang=\"").Append(H(Tr.Current.Code)).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src 'self' file: data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'\">")
            .Append("<title>").Append(H(name)).Append("</title><style>").Append(Css).Append("</style></head><body>")
            .Append("<header><h1>").Append(H(name)).Append("</h1><p class=\"hint\">")
            .Append(H(Tr.T("Pour regarder un film, le mieux est de lancer Cinedex.exe, à la racine du disque : il lit les films avec VLC, fourni sur le disque. Un navigateur ne lit pas tous les formats (son AC3 ou DTS, vidéo HEVC) : si un film ne se lit pas ici, ouvrez-le avec VLC.")))
            .Append("</p><div class=\"bar\"><input id=\"q\" type=\"search\" autocomplete=\"off\"><select id=\"genre\"></select><select id=\"sort\"></select><span id=\"count\"></span></div></header>")
            .Append("<main id=\"grid\"></main><dialog id=\"card\"></dialog>")
            .Append("<script type=\"application/json\" id=\"data\">").Append(data).Append("</script>")
            .Append("<script>").Append(Script).Append("</script></body></html>\n");
        return page.ToString();
    }

    private static string? Join(IEnumerable<string> names)
    {
        var text = string.Join(", ", names);
        return text.Length == 0 ? null : text;
    }

    /// <summary>Lien relatif vers un fichier de la racine du disque, chaque segment encodé (espaces, #, %, accents).</summary>
    public static string ToUrl(string relativePath) =>
        string.Join('/', relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static string Link(string root, string path) => ToUrl(Path.GetRelativePath(root, path));

    private const string Css = """
        :root{color-scheme:light dark;--bg:#f7f7f5;--surface:#fff;--ink:#161616;--muted:#5f6066;--line:#e6e6e3;--accent:#9b1c2e}
        @media (prefers-color-scheme:dark){:root{--bg:#121212;--surface:#1b1b1b;--ink:#f2f2f2;--muted:#ababab;--line:#2e2e2e;--accent:#e2707f}}
        *{box-sizing:border-box}body{margin:0;font:15px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif;background:var(--bg);color:var(--ink)}
        header{position:sticky;top:0;z-index:1;padding:16px 24px;background:var(--bg);border-bottom:1px solid var(--line)}
        h1{margin:0 0 4px;font-size:24px}.hint{margin:0 0 12px;color:var(--muted);font-size:13px;max-width:900px}
        .bar{display:flex;flex-wrap:wrap;gap:8px;align-items:center}
        input,select{font:inherit;padding:6px 10px;border:1px solid var(--line);border-radius:6px;background:var(--surface);color:var(--ink)}
        input{flex:1 1 220px}#count{color:var(--muted);font-size:13px}
        main{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:16px;padding:24px}
        .film{all:unset;cursor:pointer;display:flex;flex-direction:column;gap:6px}
        .film:focus-visible .poster{outline:2px solid var(--accent);outline-offset:2px}
        .poster{aspect-ratio:2/3;width:100%;border-radius:6px;background:var(--line);object-fit:cover;display:block}
        .film b{font-size:13px}.film span{font-size:12px;color:var(--muted)}
        dialog{max-width:820px;width:calc(100% - 32px);border:1px solid var(--line);border-radius:8px;background:var(--surface);color:var(--ink);padding:24px}
        dialog::backdrop{background:rgba(0,0,0,.55)}.detail{display:flex;gap:20px;flex-wrap:wrap}.detail img{width:200px;border-radius:6px}
        .detail div{flex:1 1 300px}.detail h2{margin:0}.meta{color:var(--muted);font-size:13px}.links a{display:inline-block;margin:8px 12px 0 0;color:var(--accent)}
        button.close{float:right;font:inherit;border:1px solid var(--line);background:none;color:var(--ink);border-radius:6px;padding:4px 10px;cursor:pointer}
        """;

    private const string Script = """
        (function(){
        var raw=JSON.parse(document.getElementById('data').textContent),L=raw.labels,films=raw.films;
        function el(tag,cls,text){var e=document.createElement(tag);if(cls)e.className=cls;if(text!=null)e.textContent=text;return e;}
        var q=document.getElementById('q'),genre=document.getElementById('genre'),sort=document.getElementById('sort'),grid=document.getElementById('grid'),card=document.getElementById('card'),count=document.getElementById('count');
        q.placeholder=L.search;
        var genres={};films.forEach(function(f){(f.Genres||'').split(',').forEach(function(g){g=g.trim();if(g)genres[g]=1;});});
        genre.appendChild(new Option(L.allGenres,''));Object.keys(genres).sort().forEach(function(g){genre.appendChild(new Option(g,g));});
        [['title',L.sortTitle],['year',L.sortYear],['imdb',L.sortImdb]].forEach(function(s){sort.appendChild(new Option(s[1],s[0]));});
        function folder(u){var i=u.lastIndexOf('/');return i>0?u.substring(0,i)+'/':'./';}
        function show(f){
          card.textContent='';var close=el('button','close',L.close);close.onclick=function(){card.close();};card.appendChild(close);
          var d=el('div','detail');if(f.Poster){var img=el('img');img.src=f.Poster;img.alt='';d.appendChild(img);}
          var t=el('div');t.appendChild(el('h2',null,f.Title));
          var meta=[f.Year,f.Runtime,f.Genres].filter(Boolean).join(' · ');if(meta)t.appendChild(el('p','meta',meta));
          if(f.OriginalTitle)t.appendChild(el('p','meta',f.OriginalTitle));
          var notes=[f.Imdb?'IMDb '+f.Imdb.toFixed(1):null,f.Tmdb?'TMDb '+f.Tmdb.toFixed(1):null].filter(Boolean).join(' · ');if(notes)t.appendChild(el('p','meta',notes));
          if(f.Tagline)t.appendChild(el('p',null,f.Tagline));if(f.Overview)t.appendChild(el('p',null,f.Overview));
          if(f.Directors)t.appendChild(el('p','meta',L.directors+' : '+f.Directors));if(f.Cast)t.appendChild(el('p','meta',L.cast+' : '+f.Cast));
          var links=el('p','links');
          if(f.Files.length===0)links.appendChild(el('span','meta',L.noFile));
          f.Files.forEach(function(u){var a=el('a',null,L.open);a.href=u;links.appendChild(a);var b=el('a',null,L.folder);b.href=folder(u);links.appendChild(b);});
          t.appendChild(links);d.appendChild(t);card.appendChild(d);card.showModal();
        }
        function render(){
          var text=q.value.trim().toLowerCase(),g=genre.value,s=sort.value;
          var list=films.filter(function(f){var hay=[f.Title,f.OriginalTitle,f.Directors,f.Cast].join(' ').toLowerCase();return(!text||hay.indexOf(text)>=0)&&(!g||(f.Genres||'').split(',').map(function(x){return x.trim();}).indexOf(g)>=0);});
          list.sort(function(a,b){if(s==='year')return(b.Year||0)-(a.Year||0);if(s==='imdb')return(b.Imdb||-1)-(a.Imdb||-1);return a.Title.localeCompare(b.Title);});
          grid.textContent='';
          list.forEach(function(f){var b=el('button','film');var img=el('img','poster');img.alt='';img.loading='lazy';if(f.Poster)img.src=f.Poster;b.appendChild(img);b.appendChild(el('b',null,f.Title));b.appendChild(el('span',null,[f.Year,f.Runtime].filter(Boolean).join(' · ')));b.onclick=function(){show(f);};grid.appendChild(b);});
          count.textContent=L.count.replace('{0}',list.length);
        }
        q.oninput=genre.onchange=sort.onchange=render;render();
        })();
        """;
}
