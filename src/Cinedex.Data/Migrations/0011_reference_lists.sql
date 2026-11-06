-- Listes de référence : Top 250 IMDb, palmarès, et listes personnelles ordonnées.
-- Une colonne imdb_top250 dans la table des titres ne saurait porter qu'un seul classement ; une table
-- sépare la liste (ce que le monde dit) du catalogue (ce que vous avez), et le rapprochement se calcule.

CREATE TABLE reference_list (
    id          INTEGER PRIMARY KEY,
    name        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    source      TEXT    NOT NULL,          -- imdb_top250, manual...
    note        TEXT,                      -- d'où vient la liste, et ce qu'elle vaut
    updated_at  TEXT
);

CREATE TABLE reference_entry (
    list_id INTEGER NOT NULL REFERENCES reference_list(id) ON DELETE CASCADE,
    rank    INTEGER NOT NULL,              -- 1 = en tête ; une liste non classée garde l'ordre de saisie
    imdb_id TEXT CHECK (imdb_id IS NULL OR imdb_id GLOB 'tt[0-9]*'),
    tmdb_id INTEGER,
    title   TEXT    NOT NULL,
    year    INTEGER,
    rating  REAL,
    votes   INTEGER,
    PRIMARY KEY (list_id, rank)
);
CREATE INDEX ix_reference_entry_imdb ON reference_entry (imdb_id);
CREATE INDEX ix_reference_entry_tmdb ON reference_entry (tmdb_id);
