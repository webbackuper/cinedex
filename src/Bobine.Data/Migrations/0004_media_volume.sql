-- MediaID : nom du disque (USB...) sur lequel le fichier a été trouvé. Deux disques différents peuvent porter
-- un fichier au même chemin (même lettre de lecteur à des moments différents) : le nom du disque fait partie de l'unicité.

ALTER TABLE media_file ADD COLUMN volume_label TEXT;

DROP INDEX ux_media_file_path;
CREATE UNIQUE INDEX ux_media_file_path ON media_file (coalesce(scan_root_id, 0), path, coalesce(volume_label, ''));
CREATE INDEX ix_media_file_volume ON media_file (volume_label);
