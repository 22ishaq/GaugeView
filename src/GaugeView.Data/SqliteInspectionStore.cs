using System.Globalization;
using GaugeView.Core;
using GaugeView.Core.History;
using Microsoft.Data.Sqlite;

namespace GaugeView.Data;

/// <summary>
/// Inspection history in a single SQLite file. Each save is one transaction, every value goes through a
/// parameter (never string concatenation), and the schema version lives in <c>PRAGMA user_version</c> so
/// older databases are upgraded in place and newer ones are refused rather than damaged.
/// </summary>
/// <remarks>
/// Measured values and tolerances are stored as text in invariant culture, not as SQLite REAL, so a reading of
/// 12.010 comes back as exactly 12.010. Times are stored as UTC ISO 8601, which also sorts correctly as text.
/// </remarks>
public sealed class SqliteInspectionStore : IInspectionHistory
{
    /// <summary>The schema version this build creates and understands.</summary>
    public const int SchemaVersion = 1;

    // Migrations[i] upgrades a database from schema version i to i + 1. Add new steps at the end;
    // never change a step that has shipped, because existing databases have already run it.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE inspection (
            id                INTEGER PRIMARY KEY,
            part_number       TEXT    NOT NULL,
            description       TEXT    NOT NULL,
            unit              TEXT    NOT NULL,
            operator          TEXT    NOT NULL,
            batch             TEXT    NOT NULL,
            inspected_at      TEXT    NOT NULL,
            warning_threshold TEXT    NOT NULL,
            part_count        INTEGER NOT NULL,
            pass_count        INTEGER NOT NULL,
            warning_count     INTEGER NOT NULL,
            fail_count        INTEGER NOT NULL,
            missing_count     INTEGER NOT NULL
        );
        CREATE INDEX ix_inspection_part_time ON inspection (part_number, inspected_at);

        CREATE TABLE feature (
            inspection_id   INTEGER NOT NULL REFERENCES inspection (id),
            position        INTEGER NOT NULL,
            name            TEXT    NOT NULL,
            nominal         TEXT    NOT NULL,
            upper_deviation TEXT    NOT NULL,
            lower_deviation TEXT    NOT NULL,
            PRIMARY KEY (inspection_id, position)
        );

        CREATE TABLE reading (
            inspection_id    INTEGER NOT NULL REFERENCES inspection (id),
            part_position    INTEGER NOT NULL,
            serial           TEXT    NOT NULL,
            feature_position INTEGER NOT NULL,
            measured         TEXT,
            status           TEXT    NOT NULL,
            PRIMARY KEY (inspection_id, part_position, feature_position),
            FOREIGN KEY (inspection_id, feature_position) REFERENCES feature (inspection_id, position)
        );
        """,
    ];

    private readonly string connectionString;

    /// <summary>Opens the history at <paramref name="path"/>, creating or upgrading it as needed.</summary>
    /// <exception cref="InvalidDataException">The file was created by a newer version of GaugeView.</exception>
    public SqliteInspectionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Location = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(Location);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // No pooling: the file is released after every call, so it can be copied, backed up or deleted freely.
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Location,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        Migrate();
    }

    public string Location { get; }

    /// <summary>The schema version of the file on disk.</summary>
    public int StoredSchemaVersion
    {
        get
        {
            using var connection = Open();
            return ReadSchemaVersion(connection);
        }
    }

    public InspectionRecord Save(
        InspectionPlan plan,
        IReadOnlyList<PartInspection> parts,
        string operatorName,
        string batch,
        decimal warningThreshold,
        DateTimeOffset inspectedAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(batch);
        if (parts.Count == 0)
            throw new ArgumentException("There are no parts to save.", nameof(parts));

        var featurePositions = plan.Features.Select((feature, i) => (feature, i)).ToDictionary(x => x.feature, x => x.i);
        int Count(InspectionStatus verdict) => parts.Count(p => p.Verdict == verdict);

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var insert = Command(connection, transaction, """
            INSERT INTO inspection (part_number, description, unit, operator, batch, inspected_at, warning_threshold,
                                    part_count, pass_count, warning_count, fail_count, missing_count)
            VALUES ($part, $description, $unit, $operator, $batch, $at, $threshold, $parts, $pass, $warn, $fail, $missing);
            """))
        {
            insert.Parameters.AddWithValue("$part", plan.PartNumber);
            insert.Parameters.AddWithValue("$description", plan.Description);
            insert.Parameters.AddWithValue("$unit", plan.Unit);
            insert.Parameters.AddWithValue("$operator", operatorName.Trim());
            insert.Parameters.AddWithValue("$batch", batch.Trim());
            insert.Parameters.AddWithValue("$at", FormatTime(inspectedAt));
            insert.Parameters.AddWithValue("$threshold", Text(warningThreshold));
            insert.Parameters.AddWithValue("$parts", parts.Count);
            insert.Parameters.AddWithValue("$pass", Count(InspectionStatus.Pass));
            insert.Parameters.AddWithValue("$warn", Count(InspectionStatus.Warning));
            insert.Parameters.AddWithValue("$fail", Count(InspectionStatus.Fail));
            insert.Parameters.AddWithValue("$missing", Count(InspectionStatus.Missing));
            insert.ExecuteNonQuery();
        }

        var id = Convert.ToInt64(Scalar(connection, transaction, "SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);

        using (var insert = Command(connection, transaction, """
            INSERT INTO feature (inspection_id, position, name, nominal, upper_deviation, lower_deviation)
            VALUES ($id, $position, $name, $nominal, $upper, $lower);
            """))
        {
            var position = insert.Parameters.Add("$position", SqliteType.Integer);
            var name = insert.Parameters.Add("$name", SqliteType.Text);
            var nominal = insert.Parameters.Add("$nominal", SqliteType.Text);
            var upper = insert.Parameters.Add("$upper", SqliteType.Text);
            var lower = insert.Parameters.Add("$lower", SqliteType.Text);
            insert.Parameters.AddWithValue("$id", id);
            insert.Prepare();

            for (var i = 0; i < plan.Features.Count; i++)
            {
                var feature = plan.Features[i];
                position.Value = i;
                name.Value = feature.Name;
                nominal.Value = Text(feature.Tolerance.Nominal);
                upper.Value = Text(feature.Tolerance.UpperDeviation);
                lower.Value = Text(feature.Tolerance.LowerDeviation);
                insert.ExecuteNonQuery();
            }
        }

        using (var insert = Command(connection, transaction, """
            INSERT INTO reading (inspection_id, part_position, serial, feature_position, measured, status)
            VALUES ($id, $part, $serial, $feature, $measured, $status);
            """))
        {
            var partPosition = insert.Parameters.Add("$part", SqliteType.Integer);
            var serial = insert.Parameters.Add("$serial", SqliteType.Text);
            var featurePosition = insert.Parameters.Add("$feature", SqliteType.Integer);
            var measured = insert.Parameters.Add("$measured", SqliteType.Text);
            var status = insert.Parameters.Add("$status", SqliteType.Text);
            insert.Parameters.AddWithValue("$id", id);
            insert.Prepare();

            for (var p = 0; p < parts.Count; p++)
            {
                foreach (var result in parts[p].Results)
                {
                    if (!featurePositions.TryGetValue(result.Feature, out var f))
                        throw new ArgumentException($"Feature '{result.Feature.Name}' is not in plan {plan.PartNumber}.", nameof(parts));

                    partPosition.Value = p;
                    serial.Value = parts[p].Serial;
                    featurePosition.Value = f;
                    measured.Value = result.MeasuredValue is { } value ? Text(value) : DBNull.Value;
                    status.Value = result.Status.ToString();
                    insert.ExecuteNonQuery();
                }
            }
        }

        transaction.Commit();
        return new InspectionRecord(
            id, plan.PartNumber, operatorName.Trim(), batch.Trim(), ParseTime(FormatTime(inspectedAt)), parts.Count,
            Count(InspectionStatus.Pass), Count(InspectionStatus.Warning), Count(InspectionStatus.Fail), Count(InspectionStatus.Missing));
    }

    public IReadOnlyList<InspectionRecord> List(string? partNumber = null)
    {
        using var connection = Open();
        using var query = Command(connection, null, """
            SELECT id, part_number, operator, batch, inspected_at,
                   part_count, pass_count, warning_count, fail_count, missing_count
            FROM inspection
            WHERE $part IS NULL OR part_number = $part COLLATE NOCASE
            ORDER BY inspected_at DESC, id DESC;
            """);
        query.Parameters.AddWithValue("$part", (object?)partNumber?.Trim() ?? DBNull.Value);

        var records = new List<InspectionRecord>();
        using var reader = query.ExecuteReader();
        while (reader.Read())
            records.Add(ReadRecord(reader));
        return records;
    }

    public SavedInspection Load(long id)
    {
        using var connection = Open();

        InspectionRecord record;
        string description, unit;
        decimal threshold;
        using (var query = Command(connection, null, """
            SELECT id, part_number, operator, batch, inspected_at,
                   part_count, pass_count, warning_count, fail_count, missing_count,
                   description, unit, warning_threshold
            FROM inspection WHERE id = $id;
            """))
        {
            query.Parameters.AddWithValue("$id", id);
            using var reader = query.ExecuteReader();
            if (!reader.Read())
                throw new KeyNotFoundException($"There is no saved inspection {id}.");

            record = ReadRecord(reader);
            description = reader.GetString(10);
            unit = reader.GetString(11);
            threshold = ParseDecimal(reader.GetString(12));
        }

        var features = new List<Feature>();
        using (var query = Command(connection, null, """
            SELECT name, nominal, upper_deviation, lower_deviation
            FROM feature WHERE inspection_id = $id ORDER BY position;
            """))
        {
            query.Parameters.AddWithValue("$id", id);
            using var reader = query.ExecuteReader();
            while (reader.Read())
                features.Add(ReadFeature(reader, 0));
        }

        var parts = new List<PartInspection>();
        using (var query = Command(connection, null, """
            SELECT part_position, serial, feature_position, measured, status
            FROM reading WHERE inspection_id = $id ORDER BY part_position, feature_position;
            """))
        {
            query.Parameters.AddWithValue("$id", id);
            using var reader = query.ExecuteReader();
            var current = -1;
            var serial = string.Empty;
            var results = new List<FeatureResult>();
            while (reader.Read())
            {
                var partPosition = reader.GetInt32(0);
                if (partPosition != current)
                {
                    if (current >= 0)
                        parts.Add(new PartInspection(serial, results));
                    current = partPosition;
                    serial = reader.GetString(1);
                    results = [];
                }

                results.Add(new FeatureResult(
                    features[reader.GetInt32(2)],
                    reader.IsDBNull(3) ? null : ParseDecimal(reader.GetString(3)),
                    Enum.Parse<InspectionStatus>(reader.GetString(4))));
            }

            if (current >= 0)
                parts.Add(new PartInspection(serial, results));
        }

        return new SavedInspection(record, new InspectionPlan(record.PartNumber, description, unit, features), parts, threshold);
    }

    public IReadOnlyList<FeatureTrendPoint> Trend(string partNumber, string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        using var connection = Open();
        using var query = Command(connection, null, """
            SELECT i.id, i.batch, i.operator, i.inspected_at,
                   f.name, f.nominal, f.upper_deviation, f.lower_deviation,
                   r.measured, r.status
            FROM inspection i
            JOIN feature f ON f.inspection_id = i.id AND f.name = $feature COLLATE NOCASE
            LEFT JOIN reading r ON r.inspection_id = i.id AND r.feature_position = f.position
            WHERE i.part_number = $part COLLATE NOCASE
            ORDER BY i.inspected_at, i.id, r.part_position;
            """);
        query.Parameters.AddWithValue("$part", partNumber.Trim());
        query.Parameters.AddWithValue("$feature", featureName.Trim());

        var batches = new List<TrendBatch>();
        using (var reader = query.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (batches.Count == 0 || batches[^1].Id != id)
                {
                    batches.Add(new TrendBatch(
                        id, reader.GetString(1), reader.GetString(2), ParseTime(reader.GetString(3)), ReadFeature(reader, 4)));
                }

                var batch = batches[^1];
                if (!reader.IsDBNull(8))
                    batch.Values.Add(ParseDecimal(reader.GetString(8)));
                if (!reader.IsDBNull(9) && reader.GetString(9) == nameof(InspectionStatus.Fail))
                    batch.Fails++;
            }
        }

        return batches.Select(b => b.ToPoint()).ToList();
    }

    private void Migrate()
    {
        using var connection = Open();
        var version = ReadSchemaVersion(connection);
        if (version > SchemaVersion)
        {
            throw new InvalidDataException(
                $"The history database '{Location}' is schema version {version}, which was made by a newer GaugeView. " +
                $"This version understands up to version {SchemaVersion}.");
        }

        if (version == SchemaVersion)
            return;

        using var transaction = connection.BeginTransaction();
        foreach (var step in Migrations.Skip(version))
        {
            using var command = Command(connection, transaction, step);
            command.ExecuteNonQuery();
        }

        // PRAGMA can't take a parameter; the value is a compile-time constant.
        using (var command = Command(connection, transaction, $"PRAGMA user_version = {SchemaVersion};"))
            command.ExecuteNonQuery();

        transaction.Commit();
    }

    private static int ReadSchemaVersion(SqliteConnection connection) =>
        Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        return command.ExecuteScalar();
    }

    private static InspectionRecord ReadRecord(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        ParseTime(reader.GetString(4)),
        reader.GetInt32(5),
        reader.GetInt32(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetInt32(9));

    private static Feature ReadFeature(SqliteDataReader reader, int first) => new(
        reader.GetString(first),
        new Tolerance(
            ParseDecimal(reader.GetString(first + 1)),
            ParseDecimal(reader.GetString(first + 2)),
            ParseDecimal(reader.GetString(first + 3))));

    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string text) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string FormatTime(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed class TrendBatch(long id, string batch, string operatorName, DateTimeOffset inspectedAt, Feature feature)
    {
        public long Id { get; } = id;

        public List<decimal> Values { get; } = [];

        public int Fails { get; set; }

        public FeatureTrendPoint ToPoint()
        {
            var measured = Values.Count > 0;
            var capability = CapabilityCalculator.Calculate(feature, Values);
            return new FeatureTrendPoint(
                Id, batch, operatorName, inspectedAt, Values.Count,
                measured ? capability.Mean : null,
                measured ? Values.Min() : null,
                measured ? Values.Max() : null,
                capability.Cpk,
                Fails);
        }
    }
}
