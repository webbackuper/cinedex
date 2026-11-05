-- Plusieurs entrées peuvent désigner le même film TMDb.
--
-- Cinedex catalogue des fichiers vidéo : deux sources d'un même film ont chacune leur entrée, que
-- « Fusionner les films en double » regroupe ensuite. La contrainte UNIQUE (kind, tmdb_id), héritée du temps
-- où une entrée valait un film, l'interdisait. SQLite ne sait pas retirer une contrainte : la table est
-- reconstruite à l'identique, sans elle, puis l'index qu'elle fournissait est recréé à part.
-- Les clés étrangères sont relâchées par le migrateur le temps de l'échange (un pragma est sans effet ici).

CREATE TABLE title_new (
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
    countries           TEXT,
    spoken_languages    TEXT,
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
    is_owned            INTEGER NOT NULL DEFAULT 1,
    is_favorite         INTEGER NOT NULL DEFAULT 0,
    is_shortlisted      INTEGER NOT NULL DEFAULT 0,
    is_locked           INTEGER NOT NULL DEFAULT 0,
    is_adult            INTEGER NOT NULL DEFAULT 0,
    comments            TEXT,
    added_at            TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at          TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    metadata_fetched_at TEXT
);

INSERT INTO title_new SELECT
    id, kind, title, original_title, alternative_title, sort_title, year, runtime_minutes, tagline, overview,
    overview_long, certification, countries, spoken_languages, studio, web_link, trailer_url, tmdb_id, imdb_id,
    tmdb_rating, tmdb_votes, imdb_rating, imdb_votes, user_rating, metascore, tomatometer, popcornmeter,
    imdb_top250, awards, series_status, collection_id, is_owned, is_favorite, is_shortlisted, is_locked,
    is_adult, comments, added_at, updated_at, metadata_fetched_at
FROM title;

DROP TABLE title;
ALTER TABLE title_new RENAME TO title;

-- Index repris de la table d'origine, plus celui que la contrainte d'unicité assurait jusqu'ici.
CREATE INDEX ix_title_sort ON title (sort_title);
CREATE INDEX ix_title_year ON title (year);
CREATE INDEX ix_title_imdb ON title (imdb_id);
CREATE INDEX ix_title_collection ON title (collection_id);
CREATE INDEX ix_title_tmdb ON title (kind, tmdb_id);

-- L'index plein texte suit la table reconstruite : son contenu est refait à partir des titres.
INSERT INTO title_fts (title_fts) VALUES ('delete-all');
INSERT INTO title_fts (rowid, title, original_title, alternative_title, overview)
SELECT id, title, original_title, alternative_title, overview FROM title;

-- Les déclencheurs qui tenaient l'index plein texte à jour ont disparu avec l'ancienne table.
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
