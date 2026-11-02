-- Collections (sagas TMDb) : la fiche TMDb de la collection est gardée pour lister les films qui manquent au catalogue.
-- L'affiche et le décor sont rangés dans title_image (collection_id), comme ceux des titres.

ALTER TABLE collection ADD COLUMN parts TEXT;        -- films de la saga selon TMDb, en JSON
ALTER TABLE collection ADD COLUMN fetched_at TEXT;   -- dernière lecture de la fiche TMDb
