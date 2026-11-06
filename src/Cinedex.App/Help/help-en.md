# Cinedex help

## Welcome
Cinedex catalogs your video library: films and series on hard drives and USB drives, DVDs, Blu-rays and other media. It fetches entries from TMDb (summary, poster, cast, genres), reads the technical details of your files with MediaInfo, and keeps track of what you have watched and lent.

To get started:
- **Settings > Metadata**: paste your TMDb token (free on themoviedb.org, Settings > API > "API Read Access Token").
- **Add from disk** (Ctrl+H): choose a folder of films, or individual files, then **Scan** and **Import the selection**.
- Unambiguous films are linked to TMDb; for the others, the **To review** window shows you the candidates at the end.

This help opens with **F1** or the **Help** link at the bottom of the left sidebar. The search box at the top filters the topics.

## Adding films from a disk
**Add from disk** (Ctrl+H) opens the import window.
- **Browse...** then **Scan**: looks for videos in a folder (and its subfolders).
- **Choose files…**: adds one or more specific files. You can also drag files or folders into the window.
- Uncheck the files to skip, then **Import the selection**.

Import options:
- **Identify with TMDb during import**: looks up each film's entry. Otherwise films are added with their title only, and you can run "Update from TMDb" later.
- **Use the nfo file if there is one** (checked by default): title, year and TMDb or IMDb IDs are read from the .nfo file next to the video; unchecked, only the file and folder names count.
- **Technical info (MediaInfo)**: reads codecs, resolution, runtime, audio tracks and subtitles.
- **Ignore ( ) and [ ] in names**: "17 fois Cécile Cassard (Christophe Honoré - 2002).mp4" gives the title "17 fois Cécile Cassard"; the year in brackets is still read.
- Release terms (1080p, x265, TrueHD, VOSTFR, etc.) are removed from titles. The list can be edited in **Settings > Import and scan**.

Files already in the catalog are never imported twice. Samples and trailers (files that are too small, or named sample, trailer…) are skipped.

## USB drives and MediaID
Each file keeps its **MediaID**: the name of the drive it was found on (the name shown in "Properties" in Windows).
- Give each USB drive a distinct name: Cinedex then recognizes it even if its drive letter changes (E:, F:…).
- Two different drives may hold a file at the same path: they are not mixed up.
- If you play a film whose drive is unplugged, Cinedex tells you which drive to plug in.
- The MediaID can be edited in the film's details, **File** tab, and searched with advanced search.

## TMDb entries and "To review"
Cinedex links each title to its TMDb entry:
- If the .nfo or the file name contains a TMDb or IMDb ID (tt…), the right entry is taken directly. These films are processed first.
- Otherwise Cinedex searches by title and year: a single result within one year is linked automatically.
- In every other case (several results, none with the right year, no year), Cinedex does not guess: the title goes to **To review**.

The **To review** window opens by itself at the end of an import or update, and stays available from the "To review (N)" entry in the left sidebar. For each title:
- **Choose** under the right candidate (poster, year, original title, summary);
- **See on TMDb ↗** opens a candidate in the browser, **See all suggestions on TMDb ↗** opens the whole search;
- **Other search…** to search by original title or IMDb ID;
- **None of these**: the title stays in the catalog without an entry.

For a single title: **Link or update from TMDb** (Ctrl+U). For several titles: **Update from TMDb** (Ctrl+Shift+U) on the selection or on all displayed titles. An update completes existing entries and never creates duplicates. A **locked** title is never changed from the Internet.

## IMDb, Rotten Tomatoes and Metacritic ratings
- **TMDb**: rating provided with the entry.
- **IMDb**: read from IMDb's official public file (downloaded at most once a day) and, if you have a key, through OMDb.
- **Rotten Tomatoes** and **Metacritic**: through OMDb. Create a free key on omdbapi.com and paste it in **Settings > Metadata**.

If an API reaches its limit during an import, Cinedex offers to finish without it or to stop.

## A film's details
A click on a title shows its details in the right panel, whose width you set by dragging the handle between it and the list; a double-click (or Alt+E) opens the full, editable entry with its tabs:
- **Details**: titles, year, runtime, summary, countries, studio, IMDb ID, trailer.
- **You will also like**: below the cast, Cinedex suggests films from **your** catalog that are close to the one shown, with the reason for each (same saga, same director, shared keywords or genres). Everything is computed from the database, so it is offline and instant; only films you own are suggested.
- **TMDb suggestions**: a button asks TMDb what viewers of this film also liked. One call per title, the result is kept. Suggestions already in the catalog are ticked and open with a click; the others are added to your wishlist.
- The **trailer** button in the right panel opens the video in your browser. TMDb fetches it with the rest of the record; **Settings > Metadata** offers to fill in the missing ones in one pass.
- **Ratings**: your rating and the TMDb, IMDb, Rotten Tomatoes and Metacritic ratings.
- **Cast**: actors with their photo and role; a click opens the actor's page. The crew (directing, screenplay, music) follows.
- **File**: full location, MediaID, size, runtime, container, video and audio codecs, languages, subtitles. **Reread technical info** runs MediaInfo again. **Add a part** attaches a CD2, part2… to the same film.
- **Copy**: medium, case, version, location, price, and full **loan** management (lend, return, history).
- **Genres**, **Organization** (collection, tags, groups) and **Status** (watched, favorite, owned, locked, adult content, comments).

Runtimes are shown in hours and minutes.

## Actors
The actors of each TMDb entry are kept (the number can be set in **Settings > Metadata**). The **Actors** link in the left sidebar replaces the posters in the central area with everyone in the catalog (photo, name, number of titles), with a search box at the top; clicking a **Library** filter (All titles, Movies, Series…) or **Posters** brings the posters back. Clicking a person shows their photo, biography and catalog films in the right panel (clicking a film goes back to the posters on that film); a double-click opens their full page; an actor's page shows the biography, TMDb and IMDb links, and all their films in the catalog.

## Directors
Directors have their own page, like actors. In a film's details (**Cast** tab) and in the right panel, the **Directing** section shows them with their photo: a click opens their page (biography, dates, place of birth, TMDb and IMDb links, downloaded from TMDb the first time) and the list of **all the films they directed** in the catalog; clicking a film shows it in the list.

The **Directors** link in the left sidebar shows the catalog's directors in the central area, in place of the posters, sorted by number of films; the **Library** filters bring the posters back. **Download missing records** fetches the biography and photo of those who don't have them yet in one go. The link is shown or hidden in **Settings > Left sidebar**.

## Collections
A **collection** groups the films of a saga or box set (Back to the Future, The Lord of the Rings…). It is created when a film linked to TMDb that belongs to a saga is imported, or by hand in the **Organization** tab of the details.

The **Collections** link in the left sidebar opens the list of collections, each with its backdrop, poster, overview and every film of the saga in release order:
- films **in the catalog** carry a check mark; a click shows the film in the main list;
- **missing** films are dimmed with their TMDb poster; a click opens their TMDb page.

**Only incomplete collections** keeps the sagas that are missing films. **Refresh from TMDb** reloads the chosen collection, **Rename…** changes its name and **Delete the collection…** removes it (its films stay in the catalog). **Complete all from TMDb** finds by name the collections created by hand or brought over from EMDB, then downloads the missing records and backdrops.

Collection management is turned on or off in **Settings > General** (on by default). When it is off, the link disappears from the sidebar and collection records are no longer downloaded during imports.

## Series
A series linked to TMDb gets its seasons and episodes. The **Seasons and episodes** button on the details attaches the files from a folder (names S01E02 or 1x02), marks episodes as watched and plays them.

**Series tracker** (left sidebar link, with the number of series that have episodes to watch) shows for each series its progress (episodes watched out of episodes aired), the **next episode to watch** — the **Seen** button marks it and moves to the next one — and the **next announced airing**. On the right, the calendar of episodes announced for the next 60 days. **Refresh from TMDb** fetches new episodes and dates for airing series. Double-clicking a series shows it in the catalog.

## Search and sort
- The **Search…** box (Ctrl+F) searches titles and summaries.
- **Sort by** orders the list; the button next to it reverses the order.
- **List** (Ctrl+1) or **Posters** (Ctrl+2) changes the layout.
- **Esc** clears the search, the filter and the criteria.

**Advanced search** (Ctrl+Shift+F, or the link in the left sidebar) combines conditions on every field: genre, ratings, year, runtime, actor, director, MediaID, codec, audio tracks, loan, watched… Conditions are combined with AND or OR, and the number of matching films is shown as you type.

Example: Genre is Comedy, then IMDb rating > 7, then Year ≥ 2000.

**Tooltips**: turn on "Tooltip when hovering over a film" in **Settings > General** to show, when hovering over a film in the list or the posters, its title, genres, IMDb rating and the beginning of its synopsis. They are off by default.

## The left sidebar
- **Library**: quick views (watched, favorites, shortlist, lent, video files…).
- **Definition**: submenu Standard definition, High definition, Ultra HD (4K), 3D.
- **Browse**: submenus **Genres**, **Decades**, **Countries** and **Languages**. Each lists only what the catalog actually holds, so a click never lands on an empty view. One submenu acts at a time: choosing a genre releases the country, and going back to a library view releases everything.
- At the top, the **Library**: All titles, Watched, Not watched yet, Favorites, Shortlist, Movies only, Series only, Lent, Video files, **Films without info** (titles never updated from TMDb) and the Definition submenu.
- At the bottom: **To review**, **Advanced search**, **Actors**, **Directors**, **Series tracker**, **Collections**, **Appearance**, **Settings** and **Help**.

The views and links shown are chosen in **Settings > Left sidebar**.

## Loans
In the right panel or the **Copy** tab of the details: enter the borrower (names already used are suggested) and the date, then **Lend**. **Mark as returned** closes the loan. The history keeps every loan of the title; a wrong line can be deleted. The **Lent** view in the sidebar lists the titles that are out.

## .nfo files
Cinedex reads and writes .nfo files in Kodi format (Settings > .nfo files):
- on import, a .nfo next to the film completes its entry, and its IDs are used to find the right TMDb entry;
- after a TMDb update, the .nfo can be created or completed, with a .nfo.bak safety copy.

## Appearance, language and window
- **Appearance** (left sidebar) or **Settings > Appearance**: light mode, dark mode or same as the system.
- **Settings > Appearance** also sets the **icon set** (thin line, bold line, two-tone, or classic Buddha), the **menu color** (left sidebar: stone, slate, sage, lavender, dusty rose, white), the **accent color** (velvet red, blue, green, violet, amber, graphite) and the **font** (Inter included with Cinedex, system font, Segoe UI, Calibri, Verdana, Trebuchet MS, Georgia, Arial). Each swatch shows the color in light and dark theme; everything applies when saved, without restarting.
- **Interface language**: French, English, Chinese, Hindi, Spanish, Arabic, Portuguese, Russian, German, Japanese. It applies on restart, which is offered right away.
- **Window at startup**: maximized (default), full screen, or the size from last time. **F11** toggles full screen.
- The language of TMDb summaries and genres is set separately (Settings > Metadata, fr-FR by default).

## Catalogs
Several independent catalogs can coexist (for example: films, series, a relative's collection). The menu under the logo opens, creates, renames or removes them. A removed catalog is moved to the backups, never erased.

## Settings
- **General**: language, window at startup, adult content, tooltips, collections, data folder, activity log.
- **Appearance**: theme, icon set, menu color, accent color, font.
- **Film list**: columns shown in the list view (poster, year, "Series" label, medium, runtime, genres, IMDb rating, my rating, loan, favorite, watched, number). Genres and IMDb rating are hidden by default.
- **Backups** and **Export**: see below.
- **Network**: proxy for Internet access (see below).
- **Screen saver**: on or off, idle time, slideshow interval, titles under the posters.
- **Left sidebar**: views and links shown.
- **Import and scan**: video extensions, minimum size, words that make a file ignored, subfolders, terms removed from titles, options checked by default.
- **Metadata**: TMDb token, language and country, number of actors, OMDb key, IMDb file; a button updates via TMDb only the titles without a TMDb entry (never imported).
- **.nfo files** and **Playback and tools** (MediaInfo, external player, mark as watched on playback).
- **Maintenance**: upkeep of the open catalog's database (see below).
- **Statistics**: figures for the open catalog (films, series, episodes, actors, directors, watched, favorites, lent, files, file size, total film runtime, titles linked to TMDb, to review) and breakdowns by genre, decade, definition, medium and drive (MediaID).

## Screen saver
When the Cinedex window is active but neither the keyboard nor the mouse has been used for **5 minutes**, a full-screen slideshow of the catalog's posters starts: each poster is shown over the film's blurred backdrop, with its title and year, then fades into the next one. Posters play in random order, each once before starting again.

Any key, click, wheel turn or mouse movement closes the slideshow. It does not start when Cinedex is not in the foreground (for example while a film plays in another program) or when no film has a poster; titles marked as adult content are left out when they are hidden in the list.

Everything is set in **Settings > Screen saver**: on or off, idle time (1 to 240 minutes), interval between posters (2 to 300 seconds) and the title under the posters.

## Database maintenance
The **Settings > Maintenance** tab shows the location, size and contents of the open catalog's database, and offers:
- **Rename the catalog…**: the open catalog gets a new name; Settings close and it reopens right away.
- **Back up now** and **Open the backups folder**.
- **Purge the catalog**: deletes all the content of the open catalog (titles, copies, files, people, collections, loans, series, images). Its settings, reference lists, custom fields, saved filters and scan folders are kept. Two confirmations are asked; the database is backed up just before (backups folder), but images are not.
- **Remove unused actors**: people who no longer appear in any credits (the number is shown on the button).
- **Merge duplicate actors and directors**: records with the same name (ignoring accents and capitals) are grouped; two namesakes with different TMDb or IMDb records stay separate.
- **Find duplicate films**: same TMDb record or same IMDb ID; failing any identifier, same title and same year. Nothing is deleted; double-clicking a title shows it in the list.
- **Merge duplicate films**: gathers each group into a single entry, which keeps every video file (one encoding version per part). The report tells real versions from copies of the same encoding, from MediaInfo.
- **Renumber the films in the current sort order**: copies (No. column) are numbered from 1 to n in the sort order of the main list (for example Title ↑ or Date added ↓).
- **Delete empty collections, tags and groups** and **Clean up unused images** (posters, backdrops and photos nothing uses any more).
- **Check database integrity** and **Compact and optimize the database**.

These operations apply immediately, without “Save”. Each one that changes the database asks for confirmation and first makes a backup in the backups folder.

## Keyboard shortcuts
- **F1**: help
- **Ctrl+F**: search; **Ctrl+Shift+F**: advanced search; **Esc**: show all
- **Ctrl+H**: add from disk; **Alt+A**: add a title; **Ctrl+T**: search TMDb
- **Ctrl+U**: link or update from TMDb; **Ctrl+Shift+U**: update from TMDb
- **Alt+E** or double-click: edit the entry; **Del**: delete from the catalog
- **Ctrl+P**: play; **Ctrl+O**: open the file's folder
- **Ctrl+S**: watched / not watched; **Ctrl+D**: favorite; **Ctrl+R** or **F7**: a random unwatched title
- **Ctrl+1**: list; **Ctrl+2**: posters; **F5**: refresh; **F11**: full screen
- In the poster view: **arrows** to move (with **Shift** to extend the selection), **Home**/**End**, **Page Up**/**Page Down**, **Enter** to open the details, **Ctrl+A** to select all, **Ctrl+click** and **Shift+click** for multiple selection.

## Accessibility
Every button is announced by name to screen readers (Narrator, NVDA, VoiceOver), including icon-only buttons; posters in the grid are announced with their title and year. Text colors meet the recommended contrast (WCAG AA) in light and dark theme, whatever the accent or menu color. All main navigation works from the keyboard (see shortcuts).

## Large catalogs
Cinedex is built for catalogs of more than 10,000 films: only the visible rows and posters are created, posters are decoded in the background, and marking a film watched or favorite updates only its row. Performance is checked with every version on a 12,000-film catalog.

## Installation and updates
**Cinedex-win-Setup.exe** installs Cinedex for the user (no administrator rights) with a shortcut on the desktop and in the Start menu. The installed version checks at startup whether a new version is published: an **Update available** button then appears in the left sidebar; it installs in a few seconds (only changed files are downloaded) and Cinedex restarts on it. **Settings > General > Updates** lets you check on demand or turn off the check at startup. Catalogs and settings are never touched by an update. The portable version (.zip archive) is still available and is updated by hand; Linux (AppImage) and macOS (.pkg) have their own installer too.

## Backups
**Settings > Backups** makes complete backups of the open catalog: a .zip archive containing, as chosen, the **database** (records, ratings, loans, files, catalog settings), the **images** (posters, backdrops, photos) and the **shared preferences** (appearance, language, window, log), with a manifest.
- **Back up now** makes a backup right away.
- **Automatic backup**: never, at every start, once a day, a week or a month. It runs in the background while Cinedex is open.
- **Folder**: by default the "dumps" subfolder of Cinedex's backups; **Choose…** allows a USB drive or a synced folder, for example.
- **Backups kept**: beyond this number the oldest are deleted (0: all kept).
- **Restore…** replaces the catalog database with the chosen backup's (a copy of the current one is kept first) and adds missing images; Settings close and the catalog reopens.

## Cinedex on Android
The Android app (an .apk file, installed by hand, outside the Play Store) lets you browse the catalog on a phone, read-only: search, list with posters and genres, and each film's details (overview, ratings, director, cast).
1. On the computer, create a backup with the database and the images (**Settings > Backups**).
2. Copy the .zip file to the phone (USB cable, cloud, e-mail…).
3. In Cinedex on the phone, tap **Import a backup** and pick that file. A new import replaces the phone's catalog.

To install the APK, allow installing apps from unknown sources in the Android settings.

## Exporting the list
**Settings > Export** saves the film list as **CSV**, **TSV** or **Excel (.xlsx)**: the displayed list (current search, filter and sort) or the whole catalog, with the checked columns (number, title, original title, year, genres, runtime, directors, main actors, ratings, watched, favorite, medium, location, collection, loan, resolution, MediaID, files, size, TMDb and IMDb IDs, date added, synopsis). The CSV uses the system language's separator (";" in French) and opens directly in Excel. Columns and format are remembered.

## Proxy
If your network requires a proxy, turn on **Settings > Network > Use a proxy**, choose **HTTP** or **HTTPS** and enter its address as **server:port** (for example myserver.proxy.com:8080). **Test the connection** checks that TMDb answers through this proxy. All of Cinedex's connections (TMDb, OMDb, IMDb ratings, images) then go through it; the change applies when saved, without restarting. When off (the default), Cinedex follows the proxy set in the system.

## Data, backups and security
- Catalogs, images and logs are in the data folder (Settings > General > Open the folder). In portable mode, a portable.flag file next to Cinedex.exe puts the data in a data subfolder.
- A backup of the database is made automatically before each update of its format.
- **Activity log** (Settings > General, on by default): every action taken in Cinedex (add, edit, delete, watched, favorite, loan, import, TMDb update, "To review" choice, playback, catalogs, settings) is recorded with the date, time and catalog, in one file per day (journal-YYYY-MM-DD.log, kept 90 days). **Export today's log…** makes a copy; **Open the logs folder** shows all the files. The log is written in French.
- The TMDb and OMDb keys are stored in the system's secure vault (Windows Credential Manager), never in Cinedex's files.
- Cinedex never changes your video files. Deleting a title from the catalog does not touch the disk.

## What's new
### 0.6.1
- **"You will also like"**: the details panel suggests close films from your own catalog, the closest first, each with the reason for the match. It is computed from the database (genres, keywords, director, lead actors, saga, period), so it is offline and instant. A single shared genre is not enough to be suggested: it takes two genres, or a genre and a keyword, or the same director.
- **TMDb keywords**: they now have their own place in the database, apart from your tags. **Settings > Metadata** offers to fetch them in one pass for the records that have none, which makes the matches markedly finer.
- **TMDb suggestions**: on request, the details panel shows what TMDb proposes around this film. The ones you own are ticked, the others are added to the wishlist with a click.
- **Trailers**: a button in the right panel opens the trailer in your browser. It was already fetched from TMDb with the rest of the record, but nothing played it. Only known video platforms are opened. **Settings > Metadata** adds "Fill in the missing trailers", which goes back over the records already linked to TMDb that have none.
- **Browsing the catalog**: the left sidebar gains four expandable submenus, **Genres**, **Decades**, **Countries** and **Languages**, built like MediaID. They list only what the catalog holds, countries and languages are named rather than coded, and decades run from the most recent to the oldest.

### 0.4.33
- **Update a card from the card itself**: in the editor, an **Update** button next to the TMDb number fetches the record and reloads the form — description, genres, cast, poster. It follows the number shown: typing another one and updating attaches the title to a different record without leaving the editor. Unsaved entries are replaced by what comes from TMDb, and a locked title is left unchanged.

### 0.4.32
- **Duplicates follow the identifiers**: two entries are a duplicate when they share their **TMDb record or their IMDb id**. Two films with the same title and year but different identifiers are no longer paired: they are distinct films. Links propagate, so three entries linked two by two form a single group.
- Title and year are now used only for entries **without any identifier**, where nothing can contradict them: two scanned files never identified still find each other.

### 0.4.31
- **MediaIDs in alphabetical order**: the left sidebar sorts the discs by name, ignoring case, instead of by how many titles each holds.

### 0.4.30
- **Several entries may point to the same film**: the database no longer requires a TMDb number to be unique, and Cinedex no longer refuses to create an entry that already exists. Your two sources are therefore two **full twins**, both linked to TMDb and refreshable, until you merge them — where the second one used to be left without a number, and so out of reach of “Update via TMDb”.
- **Adding a duplicate by hand is allowed**: adding from TMDb a film already in the catalog creates a new entry instead of reopening the existing one. Series keep a single entry, since they gather their episodes.
- This version **migrates the database schema** (a backup is made just before, in the backups folder).

### 0.4.29
- **One file, one entry on import**: a video file of a film already in the catalog, at another location, now gets its **own entry** instead of being attached to the first one. Cinedex catalogs files: it is **“Merge duplicate films”** (Settings > Maintenance) that then gathers those entries into a single film with several sources. The second entry takes the TMDb record (overview, genres, poster, ids); so both show up among the duplicates. (Since 0.4.30 it keeps the TMDb number as well.)
- **Parts named in the file** (CD1, CD2, part2…) still join the film they continue, in order, whatever the order the folder was walked in.

### 0.4.28
- **More readable MediaID view**: the left sidebar no longer shows the number of titles next to each disc, which was truncating long names.

### 0.4.27
- **View by MediaID**: the left sidebar has a **MediaID** submenu listing your discs. A click shows only that disc's films; a Library view leaves it.
- **MediaID column**: in **Settings > Film list**, the “Location” column gives way to the **MediaID**, that is the disc holding the file. A title spread over several discs shows them all.

### 0.4.26
- **More columns in the list view**: original title, studio, age rating, collection, location and date added join the columns already offered, to be ticked in **Settings > Film list**. They are read with the rest of the row, without slowing the list down.
- **Export the displayed list**: the toolbar's **•••** menu offers “Export the displayed list…”, which opens the export already set on the current list (current search, filter and sort). The columns and the format (Excel, CSV, TSV) are chosen there.

### 0.4.25
- **Smoother search on big catalogs**: typing no longer blocks the interface. The search runs off the display thread and a newer keystroke cancels the previous one; above all, the list no longer checks on disk that each poster exists, which cost thousands of accesses per letter. On a 12,000-title catalog, preparing a search goes from 1,484 to 202 ms, and no keystroke holds up the display any more.

### 0.4.24
- **Export a person's filmography**: an actor's or director's page has an “Export the filmography” button that saves the list shown as Excel (.xlsx), CSV or TSV. Since the filmography only shows the titles in the catalog, the export covers only the films owned. The columns are those of the catalog export.

### 0.4.23
- **Empty the entry**: a button in the right panel, between “Edit” and “Link or update from TMDb”, erases everything that came from an outside source (description, ids, sites' ratings, genres, cast, poster). The title, your own entries and the video files with their MediaInfo stay; the entry joins “Films without info” and can be filled again from TMDb.
- **Poster and TMDb number editable**: in the editor, the poster can be replaced by an image from the disk or removed, and the TMDb number can be typed in — cleared, the entry detaches from TMDb; changed, it follows another entry at the next update.

### 0.4.22
- **Fold the right panel away**: it can now fold against the right edge, down to a narrow strip, to browse the posters across almost the full width. Three ways to do it: the button in the sub-bar (right of the list and poster views), the **F9** key, or dragging the handle to the right past a threshold. The strip's button, a double-click on the handle or F9 bring it back at its previous width. The state is remembered with the catalog.

### 0.4.21
- **Merge duplicate films** (Settings > Maintenance): each group of duplicates becomes a single entry that keeps every video file, one encoding version per part. The report says how many files have exactly the same MediaInfo characteristics as another of the same film, so which ones are mere copies rather than another version. No file on the disk is touched, and the database is backed up first.

### 0.4.20
- **The same film in several files no longer creates a double**: on import, a file whose title and year match a film already in the catalog is attached to it as an extra file, instead of creating a second entry. Explicit parts (CD1, CD2…) keep their number, and a remake from another year stays a separate entry.

### 0.4.19
- **Re-scanning the same folder no longer re-checks the files already imported**: a file already attached to a title shows up marked "already in the catalog" and unchecked, so "Import the selection" only takes the new ones. This holds even when the disk is recognized under another name, the case where the old detection let the file through.

### 0.4.18
- **"Films without info" goes by content**: the view (and the button in Settings > Metadata) lists the titles that have neither a synopsis nor a genre. The download date was no use: a .nfo read at scan time sets it too, without bringing anything descriptive, and those films stayed invisible.

### 0.4.17
- **"Films without info" fixed**: the view (and the button in Settings > Metadata) now finds the entries never downloaded from TMDb. A film whose TMDb or IMDb number is known — read from a .nfo or typed by hand — but whose entry was never downloaded now appears there; before, it was wrongly left out.

### 0.4.16
- **Films without info**: a Library view (left sidebar) lists the titles that have neither a synopsis nor a genre: scanned, added by hand, or identified by a .nfo that only gave ids. Either one is enough to leave the list. It can be hidden in Settings > Left sidebar, like the other views.

### 0.4.15
- **Update of never-imported media** (Settings > Metadata): a button runs the TMDb update on the titles without a TMDb entry only — scanned or added by hand. The settings close and the update starts, with its progress bar.

### 0.4.14
- **Multiple selection**: the right panel shows how many titles are selected and lists them; each selected poster carries a cross in a circle (top-right corner), which removes the title from the selection in one click.
- **Progress bars**: the TMDb update, the technical-info refresh and the folder import show a progress bar with the current step.

### 0.4.13
- **Purge the catalog** (Settings > Maintenance): empties the open catalog completely (titles, copies, people, collections, loans, images) after two confirmations; its settings are kept. The database is backed up just before; images are not.

### 0.4.12
- **People in the right panel**: in the actors or directors gallery, a click shows the person in the right panel (photo, dates, biography, TMDb and IMDb links, their films); a double-click still opens their full page.

### 0.4.11
- **Actors and directors in the central area**: the left sidebar links show people in place of the posters, with a search box at the top; the Library filters bring the posters back. Choosing a film on a person's page goes back to the posters on that film.

### 0.4.10
- **Actor icon**: a small figure replaces the "i" in the sidebar, the cast and the people pages, in every icon set (Buddha included).

### 0.4.9
- **Cinedex on Android**: a browsing app (.apk file) that imports a backup made on the computer; see "Cinedex on Android".

### 0.4.8
- First version published on GitHub: installers for Windows, Linux and macOS, and portable archives.

### 0.4.7
- Poster view: posters grow to fill the full width of the grid, with no gap on the right, and keep their shape.

### 0.4.6
- **Resizable right-hand panel**: drag the handle between the list and the details panel (300 to 760 px); the width is remembered for the catalog, and a double-click on the handle restores the default width. The poster grid adjusts how many posters fit on each row.
- The "Wishlist" link is removed from the left sidebar.

### 0.4.5
- Right-hand panel: the **genres** of the selected title are shown as chips, between the buttons and the ratings.

### 0.4.4
- **Design tokens**: every margin, spacing, corner radius, text size and exception colour now comes from a single dictionary, on a 4 px grid. Irregular spacings are aligned and the last decorative shadows removed. A test checks at every build that no hard-coded value comes back.

### 0.4.3
- **New visual direction**: neutral stone ground, near-black ink and a single velvet-red accent kept for data; selected menu and list items as a grey pill; one near-black primary button per screen; pale chips; a scale of 5 text sizes and 3 corner radii across the app. The appearance presets become "Stone" (menus) and "Velvet red" / "Amber" (accent).

### 0.4.2
- Adding from disk: **Use the nfo file if there is one** checkbox (checked by default). Unchecked, .nfo files next to the videos are ignored: title and year come from the file name.

### 0.4.1
- **Directors**: page with biography and films directed, opened with a click from a film's details (Cast tab) or the right panel; directory of directors in the left sidebar, shown or hidden in the settings.
- The "Owned" link is removed from the left sidebar.

### 0.4.0
- **Large catalogs**: virtualized list and posters (only visible items exist), posters loaded in the background, targeted updates; tested on 12,000 films.
- **Installation and automatic updates** (Cinedex-win-Setup.exe), Linux and macOS versions.
- **Series tracker**: episodes to watch, Seen button, calendar of upcoming airings.
- **Accessibility**: screen-reader names for every button, checked contrast, poster view from the keyboard.

### 0.3.1
- **Network** tab in Settings: HTTP or HTTPS proxy (server:port), off by default, with a connection test.

### 0.3.0
- **Backups** tab: complete backups (database, images, preferences as chosen), on demand or scheduled, with rotation and restore.
- **Export** tab: film list as CSV, TSV or Excel, with a choice of columns.
- **Film list** tab: choice of the columns shown in the list view.
- **Appearance** tab: four coherent icon sets, menu color, accent color and font.

### 0.2.6
- **Maintenance** tab in Settings: rename the open catalog, back up, unused actors, duplicate actors and directors, duplicate films, renumbering in the current sort order, empty collections, tags and groups, unused images, integrity check and compaction.

### 0.2.5
- **Screen saver**: full-screen slideshow of the posters after 5 minutes of inactivity while the Cinedex window is active. On/off, idle time, interval and titles are set in Settings > Screen saver.

### 0.2.4
- **Collections** (TMDb sagas): overview, poster and backdrop downloaded from TMDb, owned and missing films of each saga, and a **Collections** link in the left sidebar. Turn it on or off in Settings > General.
- More reliable connection to TMDb on networks where IPv6 is advertised but does not work.

### 0.2.3
- **Statistics** tab in Settings: key figures for the catalog and breakdowns by genre, decade, definition, medium and drive.

### 0.2.2
- Activity log: every action taken in Cinedex is recorded (on by default), and today's log can be exported (Settings > General).

### 0.2.1
- Tooltip when hovering over a film in the list: title, genres, IMDb rating and synopsis (turn it on in Settings > General).

### 0.2.0
- New name: Bobine becomes **Cinedex**, with a new icon. Data, catalogs and keys are carried over automatically.
- Interface in 10 languages, chosen in Settings; Arabic right to left.
- Built-in help (F1).
- **To review** window: no more guessed TMDb entries; known IDs are processed first, choices offered at the end, with links to TMDb.
- **MediaID**: drive name of each file, USB drives recognized whatever their letter.
- **Advanced search** on every field, combined with AND or OR.
- Full details: ratings, clickable cast, file and MediaInfo details, CD1/CD2 parts, loan management.
- Settings in tabs; customizable left sidebar with a Definition submenu; dark mode; window maximized at startup and F11.
- Runtimes in hours and minutes; IMDb, Rotten Tomatoes and Metacritic ratings.
- Faster on large catalogs (database indexes, search as you type).

### 0.1.0
- First version: catalog, folder import, TMDb entries, series, loans, several catalogs, .nfo files.
