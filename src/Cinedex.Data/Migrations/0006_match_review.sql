-- Rapprochements TMDb à vérifier : un titre pour lequel plusieurs fiches TMDb sont possibles (ou dont l'année est
-- inconnue) n'est plus lié au hasard ; ses candidats sont gardés ici jusqu'au choix de l'utilisateur.

CREATE TABLE match_review (
    title_id    INTEGER PRIMARY KEY REFERENCES title(id) ON DELETE CASCADE,
    query       TEXT NOT NULL,             -- texte cherché sur TMDb (titre et année lus)
    candidates  TEXT NOT NULL,             -- résultats TMDb proposés, en JSON
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);
