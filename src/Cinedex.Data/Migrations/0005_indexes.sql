-- Index manquants sur les jointures les plus fréquentes : l'affiche de chaque ligne de la liste, les fichiers
-- d'un exemplaire, leurs pistes, les épisodes d'une série et les recherches par genre, tag ou groupe.
-- (L'index unique de title_image porte sur des expressions et ne sert pas aux recherches par titre.)

CREATE INDEX IF NOT EXISTS ix_title_image_title ON title_image (title_id, role);
CREATE INDEX IF NOT EXISTS ix_media_file_copy ON media_file (copy_id);
CREATE INDEX IF NOT EXISTS ix_media_file_episode ON media_file (episode_id);
CREATE INDEX IF NOT EXISTS ix_audio_track_file ON audio_track (media_file_id);
CREATE INDEX IF NOT EXISTS ix_subtitle_track_file ON subtitle_track (media_file_id);
CREATE INDEX IF NOT EXISTS ix_title_genre_genre ON title_genre (genre_id);
CREATE INDEX IF NOT EXISTS ix_title_tag_tag ON title_tag (tag_id);
CREATE INDEX IF NOT EXISTS ix_title_group_group ON title_group (group_id);
CREATE INDEX IF NOT EXISTS ix_season_title ON season (title_id);
CREATE INDEX IF NOT EXISTS ix_episode_season ON episode (season_id);

ANALYZE;
