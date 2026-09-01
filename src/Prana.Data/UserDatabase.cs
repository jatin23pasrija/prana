using Microsoft.Data.Sqlite;

namespace Prana.Data;

/// <summary>
/// The database the user owns: history, settings, and later the grocery list and pending
/// requests.
/// </summary>
/// <remarks>
/// Kept entirely separate from the catalogue, per ADR-0007. Sync replaces the catalogue file
/// wholesale, and that is only safe because nothing here lives inside it. It also means a
/// corrupt catalogue costs a download and nothing a person created.
///
/// Nothing here ever leaves the device.
/// </remarks>
public sealed class UserDatabase(CataloguePaths paths)
{
    /// <summary>
    /// Migrations, in order. Each entry is applied once and the index becomes the database's
    /// user_version.
    /// </summary>
    /// <remarks>
    /// Only what F08 needs. The grocery list belongs to F16 and pending requests to F13, and
    /// inventing their schemas now would mean guessing at features that have not been designed.
    /// Adding a migration is appending to this list; changing an existing one is not allowed,
    /// because it would already have run on someone's phone.
    /// </remarks>
    private static readonly string[][] Migrations =
    [
        [
            """
            CREATE TABLE installed_catalogue (
                id            INTEGER PRIMARY KEY CHECK (id = 1),
                version       INTEGER NOT NULL,
                kind          TEXT NOT NULL,
                built_on      TEXT NOT NULL,
                installed_at  TEXT NOT NULL,
                product_count INTEGER NOT NULL
            )
            """,
            """
            CREATE TABLE scan_history (
                gtin       TEXT NOT NULL,
                scanned_at TEXT NOT NULL,
                found      INTEGER NOT NULL,
                PRIMARY KEY (gtin, scanned_at)
            )
            """,
            "CREATE INDEX ix_scan_history_time ON scan_history(scanned_at DESC)",
        ],
    ];

    public SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.User)!);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.User,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        connection.Open();
        return connection;
    }

    /// <summary>
    /// Brings the database up to date. Safe to call on every launch: already-applied migrations
    /// are skipped, so running it twice does nothing the second time.
    /// </summary>
    public int Migrate()
    {
        using var connection = Open();

        var current = UserVersion(connection);

        if (current >= Migrations.Length)
        {
            return current;
        }

        for (var version = current; version < Migrations.Length; version++)
        {
            // Each migration is one transaction. A migration interrupted halfway leaves the
            // database at its previous version rather than in a state no code expects.
            using var transaction = connection.BeginTransaction();

            foreach (var statement in Migrations[version])
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

            using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;

                // PRAGMA does not accept a parameter, and the value is a loop index rather than
                // anything a user supplied.
                stamp.CommandText = $"PRAGMA user_version = {version + 1}";
                stamp.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        return Migrations.Length;
    }

    /// <summary>Records which catalogue is installed, so the app can say so without opening it.</summary>
    public void RecordInstalledCatalogue(CatalogueStatus status, DateTimeOffset installedAt)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO installed_catalogue (id, version, kind, built_on, installed_at, product_count)
            VALUES (1, $version, $kind, $builtOn, $installedAt, $count)
            ON CONFLICT(id) DO UPDATE SET
                version = $version, kind = $kind, built_on = $builtOn,
                installed_at = $installedAt, product_count = $count
            """;

        command.Parameters.AddWithValue("$version", status.Version);
        command.Parameters.AddWithValue("$kind", status.Kind);
        command.Parameters.AddWithValue("$builtOn", status.BuiltOn);
        command.Parameters.AddWithValue("$installedAt", installedAt.ToString("O"));
        command.Parameters.AddWithValue("$count", status.ProductCount);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Records that a barcode was looked at, and whether the catalogue had it.
    /// </summary>
    /// <remarks>
    /// Local only, and the misses matter as much as the hits: they are what tells the project
    /// which products people actually want, which is the demand signal the catalogue grows by.
    /// Nothing is uploaded without the user choosing to submit it.
    /// </remarks>
    public void RecordScan(string gtin, bool found, DateTimeOffset at)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT OR REPLACE INTO scan_history (gtin, scanned_at, found)
            VALUES ($gtin, $at, $found)
            """;

        command.Parameters.AddWithValue("$gtin", gtin);
        command.Parameters.AddWithValue("$at", at.ToString("O"));
        command.Parameters.AddWithValue("$found", found ? 1 : 0);

        command.ExecuteNonQuery();
    }

    /// <summary>The most recently scanned barcodes, newest first.</summary>
    public IReadOnlyList<(string Gtin, bool Found)> RecentScans(int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT gtin, found FROM scan_history ORDER BY scanned_at DESC LIMIT $limit";

        command.Parameters.AddWithValue("$limit", limit);

        var scans = new List<(string, bool)>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            scans.Add((reader.GetString(0), reader.GetInt32(1) == 1));
        }

        return scans;
    }

    private static int UserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";

        return Convert.ToInt32(command.ExecuteScalar());
    }
}
