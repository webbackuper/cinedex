-- Schéma initial du catalogue Cinedex.
-- Les listes de référence (supports, boîtiers, formats audio...) sont des données, pas des énumérations figées.

CREATE TABLE ref_list (
    id          INTEGER PRIMARY KEY,
    kind        TEXT    NOT NULL,          -- medium, case, source, container, audio_format, service, version, theater, color, video_system, region
    code        TEXT    NOT NULL,
    label       TEXT    NOT NULL,
    sort_order  INTEGER NOT NULL DEFAULT 0,
    is_builtin  INTEGER NOT NULL DEFAULT 1,
    UNIQUE (kind, code)
);

CREATE TABLE collection (
    id          INTEGER PRIMARY KEY,
    name        TEXT    NOT NULL,
    sort_key    TEXT,
    tmdb_id     INTEGER UNIQUE,
    overview    TEXT
);

CREATE TABLE title (
    id                  INTEGER PRIMARY KEY,
    kind                TEXT    NOT NULL CHECK (kind IN ('movie', 'series')),
    title               TEXT    NOT NULL,
    original_title      TEXT,
    alternative_title   TEXT,
    sort_title          TEXT    NOT NULL,
    year                INTEGER,
    runtime_minutes     INTEGER,
    tagline             TEXT,
    overview            TEXT,
    overview_long       TEXT,
    certification       TEXT,
    countries           TEXT,              -- codes ISO 3166-1 séparés par des virgules
    spoken_languages    TEXT,              -- codes ISO 639-1 séparés par des virgules
    studio              TEXT,
    web_link            TEXT,
    trailer_url         TEXT,
    tmdb_id             INTEGER,
    imdb_id             TEXT CHECK (imdb_id IS NULL OR imdb_id GLOB 'tt[0-9]*'),
    tmdb_rating         REAL,
    tmdb_votes          INTEGER,
    imdb_rating         REAL,
    imdb_votes          INTEGER,
    user_rating         REAL CHECK (user_rating IS NULL OR (user_rating >= 0 AND user_rating <= 10)),
    metascore           INTEGER,
    tomatometer         INTEGER,
    popcornmeter        INTEGER,
    imdb_top250         INTEGER,
    awards              TEXT,
    series_status       TEXT CHECK (series_status IS NULL OR series_status IN ('unknown', 'continuing', 'ended', 'cancelled', 'renewed')),
    collection_id       INTEGER REFERENCES collection(id) ON DELETE SET NULL,
    is_owned            INTEGER NOT NULL DEFAULT 1,  -- 0 = liste de souhaits
    is_favorite         INTEGER NOT NULL DEFAULT 0,
    is_shortlisted      INTEGER NOT NULL DEFAULT 0,
    is_locked           INTEGER NOT NULL DEFAULT 0,
    is_adult            INTEGER NOT NULL DEFAULT 0,
    comments            TEXT,
    added_at            TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at          TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    metadata_fetched_at TEXT,
    UNIQUE (kind, tmdb_id)
);
CREATE INDEX ix_title_sort ON title (sort_title);
CREATE INDEX ix_title_year ON title (year);
CREATE INDEX ix_title_imdb ON title (imdb_id);
CREATE INDEX ix_title_collection ON title (collection_id);

CREATE TABLE copy (
    id              INTEGER PRIMARY KEY,
    title_id        INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    number          INTEGER UNIQUE,
    custom_number   TEXT,
    medium_id       INTEGER REFERENCES ref_list(id),
    case_id         INTEGER REFERENCES ref_list(id),
    source_id       INTEGER REFERENCES ref_list(id),
    version_id      INTEGER REFERENCES ref_list(id),
    service_id      INTEGER REFERENCES ref_list(id),
    theater_id      INTEGER REFERENCES ref_list(id),
    video_system_id INTEGER REFERENCES ref_list(id),
    region_id       INTEGER REFERENCES ref_list(id),
    color_id        INTEGER REFERENCES ref_list(id),
    disc_count      INTEGER,
    location        TEXT,
    ean             TEXT,
    price_cents     INTEGER,
    currency        TEXT,
    features        TEXT,                  -- menus, bonus, commentaires... séparés par des virgules
    is_3d           INTEGER NOT NULL DEFAULT 0,
    added_at        TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);
CREATE INDEX ix_copy_title ON copy (title_id);

CREATE TABLE scan_root (
    id              INTEGER PRIMARY KEY,
    path            TEXT    NOT NULL UNIQUE,
    kind            TEXT    NOT NULL CHECK (kind IN ('movies', 'series')),
    label           TEXT,
    scan_on_startup INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE season (
    id              INTEGER PRIMARY KEY,
    title_id        INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    season_number   INTEGER NOT NULL,      -- 0 = épisodes spéciaux
    name            TEXT,
    overview        TEXT,
    air_date        TEXT,
    is_locked       INTEGER NOT NULL DEFAULT 0,
    UNIQUE (title_id, season_number)
);

CREATE TABLE episode (
    id              INTEGER PRIMARY KEY,
    season_id       INTEGER NOT NULL REFERENCES season(id) ON DELETE CASCADE,
    episode_number  INTEGER NOT NULL,
    name            TEXT,
    overview        TEXT,
    air_date        TEXT,
    runtime_minutes INTEGER,
    tmdb_id         INTEGER,
    UNIQUE (season_id, episode_number)
);

CREATE TABLE media_file (
    id               INTEGER PRIMARY KEY,
    copy_id          INTEGER REFERENCES copy(id) ON DELETE CASCADE,
    episode_id       INTEGER REFERENCES episode(id) ON DELETE CASCADE,
    scan_root_id     INTEGER REFERENCES scan_root(id) ON DELETE SET NULL,
    path             TEXT    NOT NULL,     -- relatif à scan_root si renseigné, absolu sinon
    part_number      INTEGER NOT NULL DEFAULT 1,
    size_bytes       INTEGER,
    quick_hash       TEXT,
    container        TEXT,
    video_codec      TEXT,
    width            INTEGER,
    height           INTEGER,
    aspect_ratio     TEXT,
    frame_rate       REAL,
    bit_depth        INTEGER,
    hdr_formats      TEXT,
    duration_seconds INTEGER,
    last_seen_at     TEXT,
    is_missing       INTEGER NOT NULL DEFAULT 0,
    CHECK ((copy_id IS NULL) <> (episode_id IS NULL))
);
CREATE UNIQUE INDEX ux_media_file_path ON media_file (coalesce(scan_root_id, 0), path);

CREATE TABLE audio_track (
    id              INTEGER PRIMARY KEY,
    copy_id         INTEGER REFERENCES copy(id) ON DELETE CASCADE,
    media_file_id   INTEGER REFERENCES media_file(id) ON DELETE CASCADE,
    format_id       INTEGER REFERENCES ref_list(id),
    language        TEXT,
    channels        TEXT,
    position        INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE subtitle_track (
    id              INTEGER PRIMARY KEY,
    copy_id         INTEGER REFERENCES copy(id) ON DELETE CASCADE,
    media_file_id   INTEGER REFERENCES media_file(id) ON DELETE CASCADE,
    language        TEXT NOT NULL,
    position        INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE image (
    id          INTEGER PRIMARY KEY,
    sha256      TEXT NOT NULL UNIQUE,
    rel_path    TEXT NOT NULL,             -- relatif au dossier images du catalogue
    width       INTEGER,
    height      INTEGER,
    source_url  TEXT
);

CREATE TABLE person (
    id              INTEGER PRIMARY KEY,
    name            TEXT NOT NULL,
    sort_name       TEXT,
    tmdb_id         INTEGER UNIQUE,
    imdb_id         TEXT CHECK (imdb_id IS NULL OR imdb_id GLOB 'nm[0-9]*'),
    photo_image_id  INTEGER REFERENCES image(id) ON DELETE SET NULL
);
CREATE INDEX ix_person_name ON person (name);

CREATE TABLE credit (
    id              INTEGER PRIMARY KEY,
    title_id        INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    person_id       INTEGER NOT NULL REFERENCES person(id) ON DELETE CASCADE,
    role            TEXT NOT NULL CHECK (role IN ('actor', 'director', 'writer', 'composer')),
    character       TEXT,
    is_voice        INTEGER NOT NULL DEFAULT 0,
    is_uncredited   INTEGER NOT NULL DEFAULT 0,
    position        INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_credit_title ON credit (title_id, role, position);
CREATE INDEX ix_credit_person ON credit (person_id);

CREATE TABLE genre (
    id      INTEGER PRIMARY KEY,
    code    TEXT NOT NULL UNIQUE,
    label   TEXT NOT NULL,
    tmdb_id INTEGER UNIQUE
);
CREATE TABLE title_genre (
    title_id INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    genre_id INTEGER NOT NULL REFERENCES genre(id) ON DELETE CASCADE,
    PRIMARY KEY (title_id, genre_id)
);

CREATE TABLE tag (
    id      INTEGER PRIMARY KEY,
    name    TEXT NOT NULL UNIQUE COLLATE NOCASE,
    color   TEXT CHECK (color IS NULL OR color GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]')
);
CREATE TABLE title_tag (
    title_id INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    tag_id   INTEGER NOT NULL REFERENCES tag(id) ON DELETE CASCADE,
    PRIMARY KEY (title_id, tag_id)
);

CREATE TABLE user_group (
    id      INTEGER PRIMARY KEY,
    name    TEXT NOT NULL UNIQUE COLLATE NOCASE
);
CREATE TABLE title_group (
    title_id INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    group_id INTEGER NOT NULL REFERENCES user_group(id) ON DELETE CASCADE,
    PRIMARY KEY (title_id, group_id)
);

CREATE TABLE viewing (
    id          INTEGER PRIMARY KEY,
    title_id    INTEGER REFERENCES title(id) ON DELETE CASCADE,
    episode_id  INTEGER REFERENCES episode(id) ON DELETE CASCADE,
    viewed_at   TEXT,                      -- NULL = vu, date inconnue
    source      TEXT NOT NULL DEFAULT 'manual' CHECK (source IN ('manual', 'playback', 'import')),
    CHECK ((title_id IS NULL) <> (episode_id IS NULL))
);
CREATE INDEX ix_viewing_title ON viewing (title_id);
CREATE INDEX ix_viewing_episode ON viewing (episode_id);

CREATE TABLE loan (
    id          INTEGER PRIMARY KEY,
    copy_id     INTEGER NOT NULL REFERENCES copy(id) ON DELETE CASCADE,
    borrower    TEXT NOT NULL,
    loaned_at   TEXT NOT NULL,
    returned_at TEXT
);
CREATE INDEX ix_loan_copy ON loan (copy_id);
CREATE UNIQUE INDEX ux_loan_open ON loan (copy_id) WHERE returned_at IS NULL;

CREATE TABLE title_image (
    title_id      INTEGER REFERENCES title(id) ON DELETE CASCADE,
    season_id     INTEGER REFERENCES season(id) ON DELETE CASCADE,
    collection_id INTEGER REFERENCES collection(id) ON DELETE CASCADE,
    image_id      INTEGER NOT NULL REFERENCES image(id) ON DELETE CASCADE,
    role          TEXT NOT NULL CHECK (role IN ('thumbnail', 'poster', 'backdrop', 'banner', 'insert')),
    CHECK ((title_id IS NOT NULL) + (season_id IS NOT NULL) + (collection_id IS NOT NULL) = 1)
);
CREATE UNIQUE INDEX ux_title_image ON title_image (coalesce(title_id, 0), coalesce(season_id, 0), coalesce(collection_id, 0), role);

CREATE TABLE custom_field (
    id          INTEGER PRIMARY KEY,
    name        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    value_type  TEXT NOT NULL CHECK (value_type IN ('text', 'number', 'date', 'bool'))
);
CREATE TABLE custom_value (
    title_id        INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    custom_field_id INTEGER NOT NULL REFERENCES custom_field(id) ON DELETE CASCADE,
    value           TEXT,
    PRIMARY KEY (title_id, custom_field_id)
);

CREATE TABLE saved_filter (
    id          INTEGER PRIMARY KEY,
    name        TEXT NOT NULL UNIQUE,
    definition  TEXT NOT NULL              -- JSON
);

CREATE TABLE setting (
    key     TEXT PRIMARY KEY,
    value   TEXT
);

-- Recherche plein texte sur les titres et les synopsis.
CREATE VIRTUAL TABLE title_fts USING fts5 (
    title, original_title, alternative_title, overview,
    content = 'title', content_rowid = 'id', tokenize = 'unicode61 remove_diacritics 2'
);
CREATE TRIGGER trg_title_fts_ai AFTER INSERT ON title BEGIN
    INSERT INTO title_fts (rowid, title, original_title, alternative_title, overview)
    VALUES (new.id, new.title, new.original_title, new.alternative_title, new.overview);
END;
CREATE TRIGGER trg_title_fts_ad AFTER DELETE ON title BEGIN
    INSERT INTO title_fts (title_fts, rowid, title, original_title, alternative_title, overview)
    VALUES ('delete', old.id, old.title, old.original_title, old.alternative_title, old.overview);
END;
CREATE TRIGGER trg_title_fts_au AFTER UPDATE ON title BEGIN
    INSERT INTO title_fts (title_fts, rowid, title, original_title, alternative_title, overview)
    VALUES ('delete', old.id, old.title, old.original_title, old.alternative_title, old.overview);
    INSERT INTO title_fts (rowid, title, original_title, alternative_title, overview)
    VALUES (new.id, new.title, new.original_title, new.alternative_title, new.overview);
END;
