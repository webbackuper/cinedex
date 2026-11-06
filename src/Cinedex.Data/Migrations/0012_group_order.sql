-- Les groupes personnels deviennent des listes ordonnées : « À montrer aux enfants » a un premier film.
-- Sans position, l'ordre était celui du tri courant, donc jamais celui que l'utilisateur avait en tête.

ALTER TABLE title_group ADD COLUMN position INTEGER NOT NULL DEFAULT 0;

-- Les groupes existants reçoivent un ordre de départ : l'ordre de tri des titres, qui est celui qu'ils
-- avaient à l'écran jusqu'ici. Rien ne change donc à l'affichage tant que l'utilisateur ne réordonne pas.
UPDATE title_group SET position = (
    SELECT count(*) FROM title_group tg2 JOIN title t2 ON t2.id = tg2.title_id
    WHERE tg2.group_id = title_group.group_id
      AND (t2.sort_title, t2.id) < (SELECT t.sort_title, t.id FROM title t WHERE t.id = title_group.title_id)
);

CREATE INDEX ix_title_group_position ON title_group (group_id, position);
