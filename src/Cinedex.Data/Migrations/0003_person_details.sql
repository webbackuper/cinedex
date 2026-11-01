-- Fiches personnes : photo TMDb (chemin source), biographie, dates et lieu de naissance, métier principal.

ALTER TABLE person ADD COLUMN profile_path TEXT;
ALTER TABLE person ADD COLUMN biography TEXT;
ALTER TABLE person ADD COLUMN birthday TEXT;
ALTER TABLE person ADD COLUMN deathday TEXT;
ALTER TABLE person ADD COLUMN place_of_birth TEXT;
ALTER TABLE person ADD COLUMN known_for TEXT;
ALTER TABLE person ADD COLUMN details_fetched_at TEXT;
