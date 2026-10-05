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
  added_at       TEXT NOT NULL DEFAULT (datetime('now'))
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
  added_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

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
  col.added_at          AS added_at
FROM collection col
JOIN comics c       ON c.id = col.comic_id
JOIN series s       ON s.id = c.series_id
LEFT JOIN publishers p ON p.id = s.publisher_id;
