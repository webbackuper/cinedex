-- Mots-clés TMDb (« braquage », « vengeance », « post-apocalyptique »…) dans leur propre table.
-- Jusqu'ici ils ne pouvaient qu'atterrir dans les tags, qui sont le classement personnel de l'utilisateur :
-- deux sens différents dans la même table. Séparés, ils servent à rapprocher les films sans polluer les tags.

CREATE TABLE keyword (
    id      INTEGER PRIMARY KEY,
    name    TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    tmdb_id INTEGER UNIQUE
);

CREATE TABLE title_keyword (
    title_id   INTEGER NOT NULL REFERENCES title(id) ON DELETE CASCADE,
    keyword_id INTEGER NOT NULL REFERENCES keyword(id) ON DELETE CASCADE,
    PRIMARY KEY (title_id, keyword_id)
);
CREATE INDEX ix_title_keyword_keyword ON title_keyword (keyword_id);
