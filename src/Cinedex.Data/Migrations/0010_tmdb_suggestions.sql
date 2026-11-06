-- Suggestions de TMDb pour un titre (« les spectateurs ont aussi aimé »), gardées après le seul appel qui
-- les récupère. Sans ce cache, chaque ouverture d'une fiche coûterait un appel réseau pour la même réponse.

CREATE TABLE tmdb_suggestion (
    title_id    INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    position    INTEGER NOT NULL,
    kind        TEXT    NOT NULL CHECK (kind IN ('movie', 'series')),
    tmdb_id     INTEGER NOT NULL,
    name        TEXT    NOT NULL,
    year        INTEGER,
    poster_path TEXT,
    rating      REAL,
    PRIMARY KEY (title_id, position)
);

-- Date du dernier appel, pour savoir si un titre a déjà été interrogé (y compris quand TMDb ne propose rien).
CREATE TABLE tmdb_suggestion_fetch (
    title_id   INTEGER PRIMARY KEY REFERENCES title(id) ON DELETE CASCADE,
    fetched_at TEXT NOT NULL
);
