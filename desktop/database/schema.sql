-- Comic collection database (SQLite).
-- Catalogue tables describe comics in general (publishers, series, issues,
-- creators). The collection and wishlist tables describe what you own and want.

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS publishers (
  id    INTEGER PRIMARY KEY,
  name  TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE IF NOT EXISTS series (
  id            INTEGER PRIMARY KEY,
  name          TEXT NOT NULL COLLATE NOCASE,
  volume        TEXT NOT NULL DEFAULT '' COLLATE NOCASE,  -- e.g. "Vol. 2" or "2016"
  start_year    INTEGER,
  publisher_id  INTEGER REFERENCES publishers(id) ON DELETE SET NULL,
  follow        INTEGER,                          -- new releases: 1 = follow, 0 = don't, blank = follow if recent
  issue_total   INTEGER,                          -- how many issues the series has on Metron (0 if Metron doesn't know it)
  total_checked TEXT NOT NULL DEFAULT '',         -- "YYYY-MM-DD" that was looked up
  UNIQUE (name, volume)
);

-- One row per issue (and per cover variant) of a series.
CREATE TABLE IF NOT EXISTS comics (
  id            INTEGER PRIMARY KEY,
  series_id     INTEGER NOT NULL REFERENCES series(id) ON DELETE CASCADE,
  issue_number  TEXT NOT NULL DEFAULT '',         -- "1", "½", "Annual 3"
  issue_sort    REAL,                             -- numeric part, for sorting and gaps
  variant       TEXT NOT NULL DEFAULT '',         -- cover letter, e.g. "B"
  variant_name  TEXT NOT NULL DEFAULT '',         -- e.g. "Jim Lee virgin cover"
  title         TEXT NOT NULL DEFAULT '',         -- story title
  cover_date    TEXT NOT NULL DEFAULT '',         -- "YYYY-MM"
  format        TEXT NOT NULL DEFAULT '',         -- blank for a regular comic; "Trade Paperback", "Hardcover"…
  barcode       TEXT NOT NULL DEFAULT '',         -- UPC + add-on digits, or ISBN
  metron_id     INTEGER,
  cover_url     TEXT NOT NULL DEFAULT '',
  cover_checked INTEGER NOT NULL DEFAULT 0,         -- 1 once "Find covers" has looked for it
  cover_price   REAL,                             -- price printed on the cover, from Metron
  price_checked INTEGER NOT NULL DEFAULT 0,         -- 1 once "Find covers" has looked for the cover price
  arcs_checked  INTEGER NOT NULL DEFAULT 0,         -- 1 once "Find story arcs" has looked it up
  credits_checked INTEGER NOT NULL DEFAULT 0,       -- 1 once its writers and artists have been looked up
  key_note      TEXT NOT NULL DEFAULT '',          -- why it's a key issue, e.g. "1st appearance of Harley Quinn"; blank if not
  volume_checked INTEGER NOT NULL DEFAULT 0,        -- 1 once Metron was asked which volume it's in
  UNIQUE (series_id, issue_number, variant, variant_name)
);
CREATE INDEX IF NOT EXISTS comics_barcode ON comics (barcode);

CREATE TABLE IF NOT EXISTS creators (
  id    INTEGER PRIMARY KEY,
  name  TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE IF NOT EXISTS comic_creators (
  comic_id    INTEGER NOT NULL REFERENCES comics(id) ON DELETE CASCADE,
  creator_id  INTEGER NOT NULL REFERENCES creators(id) ON DELETE CASCADE,
  role        TEXT NOT NULL DEFAULT '',            -- "Writer", "Artist", "Cover"…
  PRIMARY KEY (comic_id, creator_id, role)
);

-- What you own. One row per comic; quantity counts copies.
CREATE TABLE IF NOT EXISTS collection (
  id             INTEGER PRIMARY KEY,
  comic_id       INTEGER NOT NULL UNIQUE REFERENCES comics(id) ON DELETE CASCADE,
  quantity       INTEGER NOT NULL DEFAULT 1 CHECK (quantity >= 1),
  condition      TEXT NOT NULL DEFAULT '',          -- "Near Mint", "Very Fine"…
  graded_by      TEXT NOT NULL DEFAULT '',          -- "CGC", "CBCS", "PGX", or blank if not slabbed
  grade          TEXT NOT NULL DEFAULT '',          -- "9.8"
  grade_label    TEXT NOT NULL DEFAULT '',          -- "Universal (blue)", "Signature Series (yellow)"…
  cert_number    TEXT NOT NULL DEFAULT '',          -- certificate number on the slab
  price_paid     REAL,                              -- for all copies together
  current_value  REAL,                              -- per copy
  purchase_date  TEXT NOT NULL DEFAULT '',          -- "YYYY-MM-DD"
  notes          TEXT NOT NULL DEFAULT '',
  is_read        INTEGER NOT NULL DEFAULT 0,        -- 1 once you've read it
  read_on        TEXT NOT NULL DEFAULT '',          -- "YYYY-MM-DD" it was marked read (blank if not known)
  location       TEXT NOT NULL DEFAULT '',          -- which box or shelf it's in, e.g. "Box 3"
  for_trade      INTEGER NOT NULL DEFAULT 0,        -- 1 if you'd trade or sell it
  asking_price   REAL,                              -- what you'd sell it for (per copy)
  ebay_value     REAL,                              -- typical eBay asking price for one copy like this (US$), an estimate
  ebay_count     INTEGER NOT NULL DEFAULT 0,        -- how many eBay listings that estimate came from
  ebay_checked   TEXT NOT NULL DEFAULT '',          -- "YYYY-MM-DD" eBay was last checked
  ebay_graded    REAL,                              -- typical eBay asking price for a CGC 9.8 copy (US$), for raw comics
  graded_checked TEXT NOT NULL DEFAULT '',          -- "YYYY-MM-DD" CGC 9.8 prices were last checked
  rating         INTEGER NOT NULL DEFAULT 0,        -- your stars, 1 to 5 (0 = not rated)
  review         TEXT NOT NULL DEFAULT '',          -- a few words on what you thought of it
  added_at       TEXT NOT NULL DEFAULT (datetime('now')),
  changed_at     TEXT NOT NULL DEFAULT ''           -- when you last changed it yourself (set by the triggers below)
);

-- Boxes and shelves you've added before putting anything in them. A box with comics in it is just
-- their collection.location, so it needn't be here too.
CREATE TABLE IF NOT EXISTS boxes (
  name      TEXT PRIMARY KEY COLLATE NOCASE,
  added_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- "Recently changed" on Home: noted when you change a comic, not when the app fills in covers or eBay estimates by itself.
CREATE TRIGGER IF NOT EXISTS collection_changed
AFTER UPDATE OF quantity, condition, graded_by, grade, grade_label, cert_number, price_paid, current_value, purchase_date,
                notes, is_read, location, for_trade, asking_price, rating, review ON collection
BEGIN
  UPDATE collection SET changed_at = datetime('now') WHERE id = NEW.id;
END;
CREATE TRIGGER IF NOT EXISTS comic_changed
AFTER UPDATE OF issue_number, variant, variant_name, title, cover_date, format, key_note ON comics
BEGIN
  UPDATE collection SET changed_at = datetime('now') WHERE comic_id = NEW.id;
END;

-- Story arcs and runs valued together, e.g. "Batman: Year One" (Batman #404-407).
-- The set's value is split evenly across its comics. A comic is in one set at most.
CREATE TABLE IF NOT EXISTS story_sets (
  id            INTEGER PRIMARY KEY,
  name          TEXT NOT NULL UNIQUE COLLATE NOCASE,
  total_value   REAL,
  notes         TEXT NOT NULL DEFAULT '',
  arc_id        INTEGER,                          -- Metron's story arc, for sets made by "Find story arcs"
  arc_total     INTEGER                           -- how many issues the whole arc has
);

CREATE TABLE IF NOT EXISTS set_comics (
  comic_id      INTEGER PRIMARY KEY REFERENCES comics(id) ON DELETE CASCADE,
  set_id        INTEGER NOT NULL REFERENCES story_sets(id) ON DELETE CASCADE
);

-- Story arcs each comic is part of, from Metron. A comic can be in several.
CREATE TABLE IF NOT EXISTS comic_arcs (
  comic_id      INTEGER NOT NULL REFERENCES comics(id) ON DELETE CASCADE,
  arc_id        INTEGER NOT NULL,
  arc_name      TEXT NOT NULL,
  PRIMARY KEY (comic_id, arc_id)
);

-- Upcoming issues of series you follow, found by "Check for new releases".
CREATE TABLE IF NOT EXISTS releases (
  metron_id     INTEGER PRIMARY KEY,
  series        TEXT NOT NULL COLLATE NOCASE,
  volume        TEXT NOT NULL DEFAULT '',
  issue_number  TEXT NOT NULL DEFAULT '',
  title         TEXT NOT NULL DEFAULT '',
  store_date    TEXT NOT NULL DEFAULT '',          -- "YYYY-MM-DD", the day it's in shops
  cover_url     TEXT NOT NULL DEFAULT ''
);

-- Comics you want. Either points at a known comic, or just names it.
CREATE TABLE IF NOT EXISTS wishlist (
  id            INTEGER PRIMARY KEY,
  comic_id      INTEGER REFERENCES comics(id) ON DELETE CASCADE,
  series_name   TEXT NOT NULL DEFAULT '',
  issue_number  TEXT NOT NULL DEFAULT '',
  priority      INTEGER NOT NULL DEFAULT 2 CHECK (priority BETWEEN 1 AND 3),  -- 1 = must have
  max_price     REAL,
  notes         TEXT NOT NULL DEFAULT '',
  story_arc     TEXT NOT NULL DEFAULT '',          -- the arc it fills a gap in, e.g. "Year One"
  deal_checked  TEXT NOT NULL DEFAULT '',          -- "YYYY-MM-DD" eBay was last searched for a deal on it
  added_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- eBay listings of wishlist comics at or under the most you'd pay.
CREATE TABLE IF NOT EXISTS ebay_deals (
  id            INTEGER PRIMARY KEY,
  wish_id       INTEGER NOT NULL REFERENCES wishlist(id) ON DELETE CASCADE,
  item_id       TEXT NOT NULL,                     -- eBay's listing number
  title         TEXT NOT NULL DEFAULT '',
  price         REAL NOT NULL,                     -- US$, before postage
  url           TEXT NOT NULL DEFAULT '',
  found_on      TEXT NOT NULL,                     -- "YYYY-MM-DD"
  dismissed     INTEGER NOT NULL DEFAULT 0,        -- 1 once you've said "not interested"
  UNIQUE (wish_id, item_id)
);

-- Each eBay estimate, so the Stats page can show which comics are rising.
CREATE TABLE IF NOT EXISTS ebay_history (
  comic_id      INTEGER NOT NULL REFERENCES comics(id) ON DELETE CASCADE,
  day           TEXT NOT NULL,                     -- "YYYY-MM-DD"
  value         REAL NOT NULL,
  PRIMARY KEY (comic_id, day)
);

-- The collection's size and value each day the app is opened, for the Stats page.
CREATE TABLE IF NOT EXISTS value_history (
  day           TEXT PRIMARY KEY,                  -- "YYYY-MM-DD"
  comics        INTEGER NOT NULL,
  total_value   REAL NOT NULL,
  total_paid    REAL NOT NULL
);

-- Your own photos of a comic (front, back, signature…). The files are in the
-- Photos folder next to the database.
CREATE TABLE IF NOT EXISTS photos (
  id            INTEGER PRIMARY KEY,
  comic_id      INTEGER NOT NULL REFERENCES comics(id) ON DELETE CASCADE,
  file_name     TEXT NOT NULL,
  caption       TEXT NOT NULL DEFAULT '',
  added_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Comics lent to someone. returned_on stays blank while it's still out.
CREATE TABLE IF NOT EXISTS loans (
  id            INTEGER PRIMARY KEY,
  comic_id      INTEGER NOT NULL REFERENCES comics(id) ON DELETE CASCADE,
  borrower      TEXT NOT NULL COLLATE NOCASE,
  lent_on       TEXT NOT NULL,                     -- "YYYY-MM-DD"
  returned_on   TEXT NOT NULL DEFAULT '',
  notes         TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS loans_comic ON loans (comic_id);

-- Everything you own, ready to show in a list.
CREATE VIEW IF NOT EXISTS v_collection AS
SELECT
  col.id                AS collection_id,
  c.id                  AS comic_id,
  s.name                AS series,
  s.volume              AS volume,
  c.issue_number        AS issue,
  c.issue_sort          AS issue_sort,
  c.variant             AS variant,
  c.variant_name        AS variant_name,
  c.title               AS title,
  p.name                AS publisher,
  c.cover_date          AS cover_date,
  c.format              AS format,
  c.barcode             AS barcode,
  c.metron_id           AS metron_id,
  c.cover_url           AS cover_url,
  c.cover_price         AS cover_price,
  col.quantity          AS quantity,
  col.condition         AS condition,
  col.graded_by         AS graded_by,
  col.grade             AS grade,
  col.grade_label       AS grade_label,
  col.cert_number       AS cert_number,
  col.price_paid        AS price_paid,
  col.current_value     AS current_value,
  col.current_value * col.quantity AS total_value,
  col.purchase_date     AS purchase_date,
  col.notes             AS notes,
  col.is_read           AS is_read,
  col.read_on           AS read_on,
  col.location          AS location,
  col.for_trade         AS for_trade,
  col.asking_price      AS asking_price,
  c.key_note            AS key_note,
  col.ebay_value        AS ebay_value,
  col.ebay_count        AS ebay_count,
  col.ebay_checked      AS ebay_checked,
  col.ebay_graded       AS ebay_graded,
  col.graded_checked    AS graded_checked,
  col.rating            AS rating,
  col.review            AS review,
  col.added_at          AS added_at,
  col.changed_at        AS changed_at
FROM collection col
JOIN comics c       ON c.id = col.comic_id
JOIN series s       ON s.id = c.series_id
LEFT JOIN publishers p ON p.id = s.publisher_id;

-- Digital comics (CBZ, CBR, PDF, EPUB) kept in the app's own folder, with where you got up to.
CREATE TABLE IF NOT EXISTS digital_files (
  id            INTEGER PRIMARY KEY,
  path          TEXT NOT NULL UNIQUE,              -- full path, usually in Documents\Comic Catalog\Digital comics
  title         TEXT NOT NULL DEFAULT '',
  kind          TEXT NOT NULL DEFAULT '',          -- CBZ, CBR, CB7, PDF or EPUB
  pages         INTEGER NOT NULL DEFAULT 0,
  page          INTEGER NOT NULL DEFAULT 0,        -- the page you're up to (0 = first)
  comic_id      INTEGER REFERENCES comics(id) ON DELETE SET NULL,
  added_at      TEXT NOT NULL DEFAULT (datetime('now')),
  last_read     TEXT NOT NULL DEFAULT '',
  folder        TEXT NOT NULL DEFAULT ''           -- the series folder you picked; '' = worked out from the title
);
CREATE INDEX IF NOT EXISTS idx_digital_comic ON digital_files(comic_id);

-- Newest videos from the YouTube channels you follow (the Videos page).
CREATE TABLE IF NOT EXISTS videos (
  video_id      TEXT PRIMARY KEY,
  channel       TEXT NOT NULL DEFAULT '',
  title         TEXT NOT NULL DEFAULT '',
  published     TEXT NOT NULL DEFAULT '',          -- ISO date and time
  thumbnail     TEXT NOT NULL DEFAULT '',
  seen          INTEGER NOT NULL DEFAULT 0
);
