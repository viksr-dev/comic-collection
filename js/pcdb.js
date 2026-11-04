// Reads the Comic Catalog app's database file (comics.db, or a copy of it) on the phone, so the
// computer's collection can be brought across without the relay. The SQLite reader (sql.js) is
// only loaded when a database file is picked.

// The same list the desktop sends through the relay. Read from the tables, so an older database
// (missing columns added later) works too.
function snapshotSql(db) {
  const has = (table, col) => db.exec(`SELECT 1 FROM pragma_table_info('${table}') WHERE name = '${col}'`).length > 0;
  const or = (table, alias, col, fallback) => (has(table, col) ? `${alias}.${col}` : fallback);
  return `SELECT s.name, s.volume, c.issue_number, c.barcode, c.metron_id,
      ${or('collection', 'col', 'location', "''")}, ${or('comics', 'c', 'key_note', "''")}, ${or('collection', 'col', 'ebay_value', 'NULL')},
      ${or('collection', 'col', 'condition', "''")}, ${or('collection', 'col', 'is_read', '0')}, ${or('collection', 'col', 'signed_by', "''")}
    FROM collection col JOIN comics c ON c.id = col.comic_id JOIN series s ON s.id = c.series_id ORDER BY c.id`;
}
let reader;

function loadReader() {
  reader ||= new Promise((resolve, reject) => {
    const s = document.createElement('script');
    s.src = 'vendor/sql/sql-wasm.js';
    s.onload = () => window.initSqlJs({ locateFile: (f) => `vendor/sql/${f}` }).then(resolve, reject);
    s.onerror = () => reject(new Error("Couldn't load the database reader. Check your signal and try again."));
    document.head.append(s);
  }).catch((err) => {
    reader = null;
    throw err;
  });
  return reader;
}

/** True when the file is a SQLite database (whatever it's called). */
export async function isDatabase(file) {
  const head = new Uint8Array(await file.slice(0, 16).arrayBuffer());
  return new TextDecoder().decode(head) === 'SQLite format 3\0';
}

/** The computer's comics, in the same shape as the list it sends through the relay. */
export async function readComputerDb(file) {
  const SQL = await loadReader();
  const db = new SQL.Database(new Uint8Array(await file.arrayBuffer()));
  try {
    let rows;
    try {
      rows = db.exec(snapshotSql(db))[0]?.values || [];
    } catch {
      throw new Error("This database isn't from the Comic Catalog app.");
    }
    const str = (v) => (v == null ? '' : String(v));
    return rows.map((r) => [str(r[0]), str(r[1]), str(r[2]), str(r[3]), r[4] == null ? null : Number(r[4]),
      str(r[5]), str(r[6]), r[7] == null ? null : Number(r[7]), str(r[8]), Number(r[9]) ? 1 : 0, str(r[10])]);
  } finally {
    db.close();
  }
}
