using Microsoft.Data.Sqlite;

namespace Prana.Data;

/// <summary>What the app knows about the catalogue it has, if it has one.</summary>
/// <param name="IsInstalled">False when no catalogue is present, which is a normal first-run state.</param>
/// <param name="Version">Catalogue version, or 0 when none is installed.</param>
/// <param name="SchemaVersion">Schema version the file was built against.</param>
/// <param name="Kind">full or starter.</param>
/// <param name="BuiltOn">The date the catalogue was built.</param>
/// <param name="ProductCount">How many products it holds.</param>
/// <param name="Attribution">Licence attribution, carried so the app can display it.</param>
public sealed record CatalogueStatus(
    bool IsInstalled,
    int Version,
    int SchemaVersion,
    string Kind,
    string BuiltOn,
    int ProductCount,
    string Attribution)
{
    public static CatalogueStatus None { get; } =
        new(false, 0, 0, string.Empty, string.Empty, 0, string.Empty);
}

/// <summary>
/// Opens the installed catalogue read-only, and refuses anything it cannot vouch for.
/// </summary>
/// <remarks>
/// Every open is read-only, which is not a precaution but a design rule: sync replaces this file
/// wholesale, and a stray write would corrupt a file the app does not own.
///
/// A missing catalogue is normal on first run. A corrupt or unreadable one is not, but it must
/// still not crash the app: someone whose download was interrupted should get a message and a
/// working app, not a boot loop.
/// </remarks>
public sealed class CatalogueConnection(CataloguePaths paths)
{
    /// <summary>
    /// The catalogue schema this build understands. A file built against a newer schema is
    /// refused rather than read hopefully, because a column that moved would be read as
    /// something else entirely.
    /// </summary>
    public const int SupportedSchemaVersion = 1;

    public bool Exists => File.Exists(paths.Catalogue);

    /// <summary>Opens a read-only connection. The caller disposes it.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.Catalogue,
            Mode = SqliteOpenMode.ReadOnly,

            // Pooling keeps a handle open after dispose, which stops sync from replacing the
            // file on Windows and hides use-after-replace bugs on Android.
            Pooling = false,
        }.ToString());

        connection.Open();
        return connection;
    }

    /// <summary>
    /// Reads what is installed, returning <see cref="CatalogueStatus.None"/> for anything it
    /// cannot use. Never throws: this runs during startup, and a bad file must not stop the app.
    /// </summary>
    public CatalogueStatus ReadStatus()
    {
        if (!Exists)
        {
            return CatalogueStatus.None;
        }

        try
        {
            using var connection = Open();

            var schema = int.TryParse(Meta(connection, "schema_version"), out var s) ? s : 0;

            if (schema != SupportedSchemaVersion)
            {
                return CatalogueStatus.None;
            }

            return new CatalogueStatus(
                IsInstalled: true,
                Version: int.TryParse(Meta(connection, "catalogue_version"), out var v) ? v : 0,
                SchemaVersion: schema,
                Kind: Meta(connection, "kind") ?? "unknown",
                BuiltOn: Meta(connection, "built_on") ?? string.Empty,
                ProductCount: Count(connection),
                Attribution: Meta(connection, "attribution") ?? string.Empty);
        }
        catch (SqliteException)
        {
            // Truncated download, half-written file, or not a database at all. The app carries
            // on without a catalogue rather than failing to start.
            return CatalogueStatus.None;
        }
    }

    /// <summary>
    /// Whether a file is a catalogue this build can actually use. Runs before a downloaded file
    /// is allowed to replace the installed one.
    /// </summary>
    public static bool IsUsable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());

            connection.Open();

            using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA integrity_check";

            if (check.ExecuteScalar() as string != "ok")
            {
                return false;
            }

            return int.TryParse(Meta(connection, "schema_version"), out var schema)
                && schema == SupportedSchemaVersion
                && Count(connection) > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static string? Meta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }

    private static int Count(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM product";

        return Convert.ToInt32(command.ExecuteScalar());
    }
}
