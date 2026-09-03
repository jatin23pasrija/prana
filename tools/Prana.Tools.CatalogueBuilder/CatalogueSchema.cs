namespace Prana.Tools.CatalogueBuilder;

/// <summary>
/// The mobile catalogue schema.
/// </summary>
/// <remarks>
/// This is a contract, not an implementation detail. The catalogue is published as a release
/// artefact and documented so third parties can build their own clients, so changing a column
/// here breaks software we do not control. Changes go through <c>schema_version</c> in the
/// manifest and through docs/CATALOGUE_FORMAT.md.
///
/// The shape is chosen for how a phone actually reads it. Lookup by barcode has to be instant,
/// so it is the primary key. Nutrition is a separate table because a product may declare more
/// than one basis and those must never be merged. Ingredient structure exists even though the
/// parsed tree is empty today, so that filling it later is an insert rather than a migration.
/// </remarks>
public static class CatalogueSchema
{
    /// <summary>
    /// Bumped whenever the shape below changes in a way a client would notice. The app refuses a
    /// catalogue whose schema version it does not understand.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// SQLite `application_id`, so `file` and other tools identify the format rather than
    /// reporting a generic database. "PRNA" as a big-endian integer.
    /// </summary>
    public const int ApplicationId = 0x50524E41;

    public static readonly string[] Statements =
    [
        // ---------------------------------------------------------------- metadata
        """
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID
        """,

        // ---------------------------------------------------------------- products
        """
        CREATE TABLE product (
            gtin                TEXT PRIMARY KEY,
            barcode_printed     TEXT NOT NULL,
            barcode_format      TEXT NOT NULL,
            name                TEXT NOT NULL,
            brand_id            TEXT REFERENCES brand(id),
            category_id         TEXT REFERENCES category(id),
            package_value       REAL,
            package_unit        TEXT,
            multipack_count     INTEGER,
            ingredients_raw     TEXT,
            verification_status TEXT NOT NULL,
            last_verified       TEXT NOT NULL,
            -- 0 when the record carries neither nutrition nor ingredients. Stored rather than
            -- derived so the app can ask for incomplete records in one indexed query, and so the
            -- rule in ADR-0026 is a fact in the data rather than a check every caller must
            -- remember to repeat.
            is_complete         INTEGER NOT NULL
        ) WITHOUT ROWID
        """,

        "CREATE INDEX ix_product_barcode ON product(barcode_printed)",
        "CREATE INDEX ix_product_category ON product(category_id) WHERE category_id IS NOT NULL",
        "CREATE INDEX ix_product_brand ON product(brand_id) WHERE brand_id IS NOT NULL",

        """
        CREATE TABLE product_country (
            gtin    TEXT NOT NULL REFERENCES product(gtin),
            country TEXT NOT NULL,
            PRIMARY KEY (gtin, country)
        ) WITHOUT ROWID
        """,

        // ---------------------------------------------------------------- nutrition
        """
        CREATE TABLE nutrition (
            gtin                TEXT NOT NULL REFERENCES product(gtin),
            block_index         INTEGER NOT NULL,
            -- per_100g, per_100ml, per_serving or per_package. Never inferred, always shown to
            -- the user beside the numbers.
            basis               TEXT NOT NULL,
            serving_description TEXT,
            serving_value       REAL,
            serving_unit        TEXT,
            energy_kcal         REAL,
            energy_kj           REAL,
            protein_g           REAL,
            carbohydrate_g      REAL,
            sugars_g            REAL,
            added_sugars_g      REAL,
            fat_g               REAL,
            saturated_fat_g     REAL,
            trans_fat_g         REAL,
            fibre_g             REAL,
            sodium_mg           REAL,
            PRIMARY KEY (gtin, block_index)
        ) WITHOUT ROWID
        """,

        // The alternatives engine compares products within a category on a comparable basis, so
        // it filters on basis before anything else.
        "CREATE INDEX ix_nutrition_basis ON nutrition(basis)",

        """
        CREATE TABLE nutrition_not_declared (
            gtin        TEXT NOT NULL,
            block_index INTEGER NOT NULL,
            field       TEXT NOT NULL,
            PRIMARY KEY (gtin, block_index, field)
        ) WITHOUT ROWID
        """,

        // ---------------------------------------------------------------- ingredients
        """
        CREATE TABLE ingredient_item (
            gtin           TEXT NOT NULL REFERENCES product(gtin),
            ordinal        INTEGER NOT NULL,
            parent_ordinal INTEGER,
            raw            TEXT NOT NULL,
            canonical_id   TEXT REFERENCES ingredient(id),
            percentage     REAL,
            PRIMARY KEY (gtin, ordinal)
        ) WITHOUT ROWID
        """,

        "CREATE INDEX ix_ingredient_item_canonical ON ingredient_item(canonical_id) WHERE canonical_id IS NOT NULL",

        """
        CREATE TABLE ingredient (
            id          TEXT PRIMARY KEY,
            name        TEXT NOT NULL,
            category    TEXT NOT NULL,
            explanation TEXT
        ) WITHOUT ROWID
        """,

        """
        CREATE TABLE ingredient_alias (
            ingredient_id TEXT NOT NULL REFERENCES ingredient(id),
            alias         TEXT NOT NULL,
            PRIMARY KEY (ingredient_id, alias)
        ) WITHOUT ROWID
        """,

        """
        CREATE TABLE ingredient_flag (
            ingredient_id TEXT NOT NULL REFERENCES ingredient(id),
            flag          TEXT NOT NULL,
            PRIMARY KEY (ingredient_id, flag)
        ) WITHOUT ROWID
        """,

        // ---------------------------------------------------------------- peer statistics
        """
        -- Cut-off values for "higher in sugar than most biscuits", precomputed at build time.
        -- Computing them on the phone would mean scanning every product in a category on every
        -- product screen, against a 300 ms budget.
        --
        -- Only categories meeting the minimum peer count in the peer_comparison rule appear
        -- here at all. A row's absence is what tells the app to say nothing, which is the
        -- correct answer for roughly 94 per cent of the catalogue: 74 per cent of products have
        -- no category, and most categories hold too few comparable values to rank against.
        CREATE TABLE category_peer_stat (
            category_id  TEXT NOT NULL REFERENCES category(id),
            basis        TEXT NOT NULL,
            nutrient     TEXT NOT NULL,
            peer_count   INTEGER NOT NULL,
            lower_value  REAL NOT NULL,
            higher_value REAL NOT NULL,
            rule_id      TEXT NOT NULL,
            rule_version TEXT NOT NULL,
            PRIMARY KEY (category_id, basis, nutrient)
        ) WITHOUT ROWID
        """,

        // ---------------------------------------------------------------- reference data
        """
        CREATE TABLE brand (
            id    TEXT PRIMARY KEY,
            name  TEXT NOT NULL,
            owner TEXT
        ) WITHOUT ROWID
        """,

        """
        CREATE TABLE category (
            id            TEXT PRIMARY KEY,
            name          TEXT NOT NULL,
            parent_id     TEXT,
            typical_basis TEXT NOT NULL
        ) WITHOUT ROWID
        """,

        """
        CREATE TABLE category_substitute (
            category_id   TEXT NOT NULL REFERENCES category(id),
            substitute_id TEXT NOT NULL,
            PRIMARY KEY (category_id, substitute_id)
        ) WITHOUT ROWID
        """,

        """
        CREATE TABLE category_nutrient (
            category_id TEXT NOT NULL REFERENCES category(id),
            field       TEXT NOT NULL,
            PRIMARY KEY (category_id, field)
        ) WITHOUT ROWID
        """,

        """
        CREATE TABLE country (
            code               TEXT PRIMARY KEY,
            name               TEXT NOT NULL,
            default_basis      TEXT NOT NULL,
            sodium_declared_as TEXT NOT NULL
        ) WITHOUT ROWID
        """,

        // ---------------------------------------------------------------- provenance
        """
        CREATE TABLE source (
            gtin         TEXT NOT NULL REFERENCES product(gtin),
            source_id    TEXT NOT NULL,
            type         TEXT NOT NULL,
            url          TEXT,
            retrieved_at TEXT NOT NULL,
            licence      TEXT,
            PRIMARY KEY (gtin, source_id)
        ) WITHOUT ROWID
        """,

        """
        CREATE TABLE provenance (
            gtin       TEXT NOT NULL REFERENCES product(gtin),
            path       TEXT NOT NULL,
            source_id  TEXT NOT NULL,
            confidence TEXT NOT NULL,
            PRIMARY KEY (gtin, path)
        ) WITHOUT ROWID
        """,

        // ---------------------------------------------------------------- search
        // Name and brand only. Indexing ingredient text would roughly double the index to answer
        // a question almost nobody asks while standing in a shop. Phase 2 can add it.
        """
        CREATE VIRTUAL TABLE product_search USING fts5(
            name,
            brand,
            gtin UNINDEXED,
            tokenize = 'unicode61 remove_diacritics 2'
        )
        """,
    ];
}
