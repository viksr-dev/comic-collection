-- Handy queries for the comic collection database (SQLite).

-- 1. Total collection value, copies and amount paid.
SELECT COUNT(*)                     AS comics,
       SUM(quantity)                AS copies,
       ROUND(SUM(total_value), 2)   AS total_value,
       ROUND(SUM(price_paid), 2)    AS total_paid
FROM v_collection;

-- 2. Value by series, most valuable first.
SELECT series, volume, COUNT(*) AS issues, ROUND(SUM(total_value), 2) AS value
FROM v_collection
GROUP BY series, volume
ORDER BY value DESC;

-- 3. Missing issues in a run: every whole number between the first and last
--    issue you own of a series that you don't have. Change the series name.
WITH RECURSIVE
  owned AS (
    SELECT DISTINCT CAST(issue_sort AS INTEGER) AS n
    FROM v_collection
    WHERE series = 'Daredevil' AND volume = '' AND issue_sort = CAST(issue_sort AS INTEGER)
  ),
  run(n) AS (
    SELECT MIN(n) FROM owned
    UNION ALL
    SELECT n + 1 FROM run WHERE n < (SELECT MAX(n) FROM owned)
  )
SELECT n AS missing_issue FROM run WHERE n NOT IN (SELECT n FROM owned);

-- 4. Series with gaps, and how many issues are missing in each.
WITH runs AS (
  SELECT series, volume,
         MIN(CAST(issue_sort AS INTEGER)) AS first,
         MAX(CAST(issue_sort AS INTEGER)) AS last,
         COUNT(DISTINCT CAST(issue_sort AS INTEGER)) AS have
  FROM v_collection
  WHERE issue_sort IS NOT NULL AND issue_sort = CAST(issue_sort AS INTEGER)
  GROUP BY series, volume
)
SELECT series, volume, first, last, have, (last - first + 1) - have AS missing
FROM runs
WHERE (last - first + 1) > have
ORDER BY missing DESC;

-- 5. Find a comic by barcode (the main 12 digits are enough).
SELECT * FROM v_collection WHERE barcode LIKE '761941354581%';

-- 6. Wishlist, must-haves first.
SELECT w.priority,
       COALESCE(s.name, w.series_name)       AS series,
       COALESCE(c.issue_number, w.issue_number) AS issue,
       w.max_price, w.notes
FROM wishlist w
LEFT JOIN comics c ON c.id = w.comic_id
LEFT JOIN series s ON s.id = c.series_id
ORDER BY w.priority, series, issue;

-- 7. Comics worth more than you paid.
SELECT series, issue, price_paid, total_value, ROUND(total_value - price_paid, 2) AS gain
FROM v_collection
WHERE price_paid IS NOT NULL AND total_value IS NOT NULL AND total_value > price_paid
ORDER BY gain DESC;

-- 8. Who wrote or drew the most comics you own.
SELECT cr.name, cc.role, COUNT(*) AS comics
FROM comic_creators cc
JOIN creators cr ON cr.id = cc.creator_id
JOIN collection col ON col.comic_id = cc.comic_id
GROUP BY cr.name, cc.role
ORDER BY comics DESC
LIMIT 20;
