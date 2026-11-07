-- Section TMDb d'où vient la fiche d'un titre. TMDb range les téléfilms sous « tv », avec leurs propres
-- numéros : un film catalogué ici peut donc porter un numéro de la section tv. Sans cette colonne, la mise
-- à jour suivante irait chercher ce numéro dans la section movie et tomberait sur un autre film, ou sur rien.
-- NULL = la section correspond au genre du titre, ce qui est le cas de tout ce qui existait avant.

ALTER TABLE title ADD COLUMN tmdb_kind TEXT CHECK (tmdb_kind IS NULL OR tmdb_kind IN ('movie', 'series'));
