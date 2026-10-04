-- Sample comics to try the database with. Prices and values are made up.
-- Safe to delete: DELETE FROM collection; DELETE FROM wishlist; DELETE FROM series; DELETE FROM publishers; DELETE FROM creators;

INSERT INTO publishers (name) VALUES ('DC Comics'), ('Marvel'), ('Image');

INSERT INTO series (name, volume, start_year, publisher_id) VALUES
  ('Batman',          '2016', 2016, (SELECT id FROM publishers WHERE name = 'DC Comics')),
  ('Daredevil',       '',     1964, (SELECT id FROM publishers WHERE name = 'Marvel')),
  ('Saga',            '',     2012, (SELECT id FROM publishers WHERE name = 'Image')),
  ('All-Star Batman', '',     2016, (SELECT id FROM publishers WHERE name = 'DC Comics'));

INSERT INTO comics (series_id, issue_number, issue_sort, title, cover_date, format) VALUES
  ((SELECT id FROM series WHERE name = 'Batman'),          '1',   1,   'I Am Gotham, Part One',  '2016-08', ''),
  ((SELECT id FROM series WHERE name = 'Batman'),          '2',   2,   'I Am Gotham, Part Two',  '2016-08', ''),
  ((SELECT id FROM series WHERE name = 'Batman'),          '4',   4,   'I Am Gotham, Part Four', '2016-09', ''),
  ((SELECT id FROM series WHERE name = 'Daredevil'),       '168', 168, 'Elektra',                '1981-01', ''),
  ((SELECT id FROM series WHERE name = 'Daredevil'),       '170', 170, 'The Kingpin Must Die!',  '1981-05', ''),
  ((SELECT id FROM series WHERE name = 'Saga'),            '1',   1,   'Chapter One',            '2012-03', ''),
  ((SELECT id FROM series WHERE name = 'All-Star Batman'), '7',   7,   'The First Ally, Part 1', '2017-04', '');

INSERT INTO collection (comic_id, quantity, condition, price_paid, current_value, purchase_date)
SELECT c.id, v.qty, v.cond, v.paid, v.value, v.bought
FROM (
  SELECT 'Batman' AS s, '1' AS i, 2 AS qty, 'Near Mint' AS cond, 12.00 AS paid, 8.00 AS value, '2016-06-15' AS bought UNION ALL
  SELECT 'Batman',          '2',   1, 'Near Mint', 5.00,  5.00,  '2016-07-06' UNION ALL
  SELECT 'Batman',          '4',   1, 'Very Fine', 5.00,  4.00,  '2016-08-03' UNION ALL
  SELECT 'Daredevil',       '168', 1, 'Fine',      60.00, 150.00, '2019-11-02' UNION ALL
  SELECT 'Daredevil',       '170', 1, 'Very Fine', 15.00, 20.00, '2019-11-02' UNION ALL
  SELECT 'Saga',            '1',   1, 'Near Mint', 4.00,  45.00, '2012-03-14'
) v
JOIN series s ON s.name = v.s
JOIN comics c ON c.series_id = s.id AND c.issue_number = v.i;

INSERT INTO creators (name) VALUES ('Tom King'), ('David Finch'), ('Frank Miller'), ('Brian K. Vaughan'), ('Fiona Staples');
INSERT INTO comic_creators (comic_id, creator_id, role)
SELECT c.id, cr.id, v.role
FROM (
  SELECT 'Batman' AS s, '1' AS i, 'Tom King' AS who, 'Writer' AS role UNION ALL
  SELECT 'Batman', '1', 'David Finch', 'Artist' UNION ALL
  SELECT 'Daredevil', '168', 'Frank Miller', 'Writer' UNION ALL
  SELECT 'Daredevil', '168', 'Frank Miller', 'Artist' UNION ALL
  SELECT 'Saga', '1', 'Brian K. Vaughan', 'Writer' UNION ALL
  SELECT 'Saga', '1', 'Fiona Staples', 'Artist'
) v
JOIN series s ON s.name = v.s
JOIN comics c ON c.series_id = s.id AND c.issue_number = v.i
JOIN creators cr ON cr.name = v.who;

-- All-Star Batman #7 is known but not owned yet: it goes on the wishlist.
INSERT INTO wishlist (comic_id, priority, max_price, notes)
VALUES ((SELECT c.id FROM comics c JOIN series s ON s.id = c.series_id WHERE s.name = 'All-Star Batman' AND c.issue_number = '7'), 1, 10.00, '');
INSERT INTO wishlist (series_name, issue_number, priority, notes) VALUES ('Daredevil', '169', 2, 'Fill the gap');
