using Microsoft.Data.Sqlite;

namespace PicSelect.Core.Projects;

public sealed class PicSelectStore
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff",
    };

    private readonly string databasePath;

    public PicSelectStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(this.databasePath)!);
        EnsureSchema();
    }

    public async Task<ImportedProject> ImportProjectFromFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var normalizedFolderPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(normalizedFolderPath))
        {
            throw new DirectoryNotFoundException($"Folder '{normalizedFolderPath}' does not exist.");
        }

        await using var connection = OpenConnection();

        var existing = await TryGetExistingProjectAsync(connection, normalizedFolderPath, cancellationToken);
        if (existing is not null)
        {
            return existing with { AlreadyExisted = true };
        }

        var files = EnumerateSupportedFiles(normalizedFolderPath);
        var importedAtUtc = DateTimeOffset.UtcNow;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var projectId = await ExecuteInsertAsync(
            connection,
            transaction,
            """
            INSERT INTO Projects (FolderPath, DisplayName, ImportedAtUtc)
            VALUES ($folderPath, $displayName, $importedAtUtc);
            SELECT last_insert_rowid();
            """,
            cancellationToken,
            ("$folderPath", normalizedFolderPath),
            ("$displayName", Path.GetFileName(normalizedFolderPath)),
            ("$importedAtUtc", importedAtUtc.ToString("O")));

        var iterationId = await ExecuteInsertAsync(
            connection,
            transaction,
            """
            INSERT INTO Iterations (ProjectId, Number, CreatedAtUtc)
            VALUES ($projectId, 1, $createdAtUtc);
            SELECT last_insert_rowid();
            """,
            cancellationToken,
            ("$projectId", projectId),
            ("$createdAtUtc", importedAtUtc.ToString("O")));

        foreach (var file in files)
        {
            var photoId = await ExecuteInsertAsync(
                connection,
                transaction,
                """
                INSERT INTO Photos (ProjectId, FilePath, FileName, SortName, SizeBytes, LastModifiedUtc)
                VALUES ($projectId, $filePath, $fileName, $sortName, $sizeBytes, $lastModifiedUtc);
                SELECT last_insert_rowid();
                """,
                cancellationToken,
                ("$projectId", projectId),
                ("$filePath", file.FullName),
                ("$fileName", file.Name),
                ("$sortName", file.Name.ToUpperInvariant()),
            ("$sizeBytes", (object)file.Length),
            ("$lastModifiedUtc", (object)file.LastWriteTimeUtc.ToString("O")));

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                INSERT INTO MembershipEvents (ProjectId, IterationId, PhotoId, EventType, CreatedAtUtc)
                VALUES ($projectId, $iterationId, $photoId, 'include', $createdAtUtc);
                """,
                cancellationToken,
                ("$projectId", (object)projectId),
                ("$iterationId", (object)iterationId),
                ("$photoId", (object)photoId),
                ("$createdAtUtc", (object)importedAtUtc.ToString("O")));
        }

        await transaction.CommitAsync(cancellationToken);
        return new ImportedProject(projectId, normalizedFolderPath, files.Count, AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                p.Id,
                p.FolderPath,
                p.DisplayName,
                p.ImportedAtUtc,
                COUNT(DISTINCT ph.Id) AS PhotoCount,
                COALESCE(MAX(i.Number), 0) AS IterationCount
            FROM Projects p
            LEFT JOIN Photos ph ON ph.ProjectId = p.Id
            LEFT JOIN Iterations i ON i.ProjectId = p.Id
            GROUP BY p.Id, p.FolderPath, p.DisplayName, p.ImportedAtUtc
            ORDER BY p.ImportedAtUtc DESC, p.Id DESC;
            """;

        var projects = new List<ProjectSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(new ProjectSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return projects;
    }

    public async Task<ProjectOverview?> GetProjectOverviewAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        using var projectCommand = connection.CreateCommand();
        projectCommand.CommandText =
            """
            SELECT Id, FolderPath, DisplayName, ImportedAtUtc
            FROM Projects
            WHERE Id = $projectId;
            """;
        projectCommand.Parameters.AddWithValue("$projectId", projectId);

        await using var projectReader = await projectCommand.ExecuteReaderAsync(cancellationToken);
        if (!await projectReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var folderPath = projectReader.GetString(1);
        var displayName = projectReader.GetString(2);
        var importedAtUtc = DateTimeOffset.Parse(projectReader.GetString(3));

        var iterations = new List<IterationSummary>();
        using var iterationCommand = connection.CreateCommand();
        iterationCommand.CommandText =
            """
            SELECT Id, Number
            FROM Iterations
            WHERE ProjectId = $projectId
            ORDER BY Number;
            """;
        iterationCommand.Parameters.AddWithValue("$projectId", projectId);

        await using var iterationReader = await iterationCommand.ExecuteReaderAsync(cancellationToken);
        while (await iterationReader.ReadAsync(cancellationToken))
        {
            var iterationId = iterationReader.GetInt64(0);
            var iterationNumber = iterationReader.GetInt32(1);
            var photos = await GetIterationPhotosByIterationIdAsync(connection, iterationId, cancellationToken);
            var chosenCount = photos.Count(photo => string.Equals(photo.DecisionType, "choose", StringComparison.OrdinalIgnoreCase));
            var ignoredCount = photos.Count(photo => string.Equals(photo.DecisionType, "ignore", StringComparison.OrdinalIgnoreCase));

            iterations.Add(new IterationSummary(
                iterationId,
                iterationNumber,
                photos.Count,
                chosenCount + ignoredCount,
                chosenCount,
                ignoredCount));
        }

        return new ProjectOverview(projectId, folderPath, displayName, importedAtUtc, iterations);
    }

    public async Task<IReadOnlyList<IterationPhoto>> GetIterationPhotosAsync(
        long projectId,
        int iterationNumber,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id
            FROM Iterations
            WHERE ProjectId = $projectId AND Number = $iterationNumber;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$iterationNumber", iterationNumber);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is null)
        {
            return Array.Empty<IterationPhoto>();
        }

        return await GetIterationPhotosByIterationIdAsync(connection, (long)(scalar), cancellationToken);
    }

    public async Task<ReviewSession?> GetReviewSessionAsync(
        long projectId,
        int iterationNumber,
        long? preferredPhotoId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var iterationId = await TryGetIterationIdAsync(connection, projectId, iterationNumber, cancellationToken);
        if (iterationId is null)
        {
            return null;
        }

        var photos = await GetIterationPhotosByIterationIdAsync(connection, iterationId.Value, cancellationToken);
        if (photos.Count == 0)
        {
            return null;
        }

        var currentPhotoId = preferredPhotoId.HasValue && photos.Any(photo => photo.PhotoId == preferredPhotoId.Value)
            ? preferredPhotoId.Value
            : photos.FirstOrDefault(photo => photo.DecisionType is null)?.PhotoId ?? photos[^1].PhotoId;

        var currentPhotoIndex = photos.FindIndex(photo => photo.PhotoId == currentPhotoId);
        if (currentPhotoIndex < 0)
        {
            currentPhotoIndex = 0;
        }

        return new ReviewSession(
            projectId,
            iterationId.Value,
            iterationNumber,
            currentPhotoIndex,
            photos.Count(photo => photo.DecisionType is not null),
            photos);
    }

    public async Task RecordDecisionAsync(
        long projectId,
        int iterationNumber,
        long photoId,
        string decisionType,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(decisionType, "choose", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(decisionType, "ignore", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(decisionType), "Decision must be 'choose' or 'ignore'.");
        }

        await using var connection = OpenConnection();
        var iterationId = await TryGetIterationIdAsync(connection, projectId, iterationNumber, cancellationToken);
        if (iterationId is null)
        {
            throw new InvalidOperationException("The requested iteration does not exist.");
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO DecisionEvents (ProjectId, IterationId, PhotoId, DecisionType, CreatedAtUtc)
            VALUES ($projectId, $iterationId, $photoId, $decisionType, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$iterationId", iterationId.Value);
        command.Parameters.AddWithValue("$photoId", photoId);
        command.Parameters.AddWithValue("$decisionType", decisionType.ToLowerInvariant());
        command.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));

        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> UndoLastDecisionAsync(
        long projectId,
        int iterationNumber,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var iterationId = await TryGetIterationIdAsync(connection, projectId, iterationNumber, cancellationToken);
        if (iterationId is null)
        {
            return null;
        }

        using var latestCommand = connection.CreateCommand();
        latestCommand.CommandText =
            """
            SELECT Id, PhotoId
            FROM DecisionEvents
            WHERE ProjectId = $projectId AND IterationId = $iterationId
            ORDER BY Id DESC
            LIMIT 1;
            """;
        latestCommand.Parameters.AddWithValue("$projectId", projectId);
        latestCommand.Parameters.AddWithValue("$iterationId", iterationId.Value);

        await using var latestReader = await latestCommand.ExecuteReaderAsync(cancellationToken);
        if (!await latestReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var latestEventId = latestReader.GetInt64(0);
        var photoId = latestReader.GetInt64(1);

        using var previousCommand = connection.CreateCommand();
        previousCommand.CommandText =
            """
            SELECT DecisionType
            FROM DecisionEvents
            WHERE ProjectId = $projectId
              AND IterationId = $iterationId
              AND PhotoId = $photoId
              AND Id < $latestEventId
            ORDER BY Id DESC
            LIMIT 1;
            """;
        previousCommand.Parameters.AddWithValue("$projectId", projectId);
        previousCommand.Parameters.AddWithValue("$iterationId", iterationId.Value);
        previousCommand.Parameters.AddWithValue("$photoId", photoId);
        previousCommand.Parameters.AddWithValue("$latestEventId", latestEventId);

        var previousDecision = (string?)await previousCommand.ExecuteScalarAsync(cancellationToken) ?? "undecided";

        using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
            """
            INSERT INTO DecisionEvents (ProjectId, IterationId, PhotoId, DecisionType, CreatedAtUtc)
            VALUES ($projectId, $iterationId, $photoId, $decisionType, $createdAtUtc);
            """;
        insertCommand.Parameters.AddWithValue("$projectId", projectId);
        insertCommand.Parameters.AddWithValue("$iterationId", iterationId.Value);
        insertCommand.Parameters.AddWithValue("$photoId", photoId);
        insertCommand.Parameters.AddWithValue("$decisionType", previousDecision);
        insertCommand.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));

        _ = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        return photoId;
    }

    public async Task<IterationSummary> CreateNextIterationAsync(
        long projectId,
        int iterationNumber,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var currentIterationId = await TryGetIterationIdAsync(connection, projectId, iterationNumber, cancellationToken);
        if (currentIterationId is null)
        {
            throw new InvalidOperationException("The requested iteration does not exist.");
        }

        var currentPhotos = await GetIterationPhotosByIterationIdAsync(connection, currentIterationId.Value, cancellationToken);
        if (currentPhotos.Any(photo => photo.DecisionType is null))
        {
            throw new InvalidOperationException("The current iteration must be fully decided before creating the next iteration.");
        }

        var nextIterationNumber = iterationNumber + 1;
        var existingNextIterationId = await TryGetIterationIdAsync(connection, projectId, nextIterationNumber, cancellationToken);
        if (existingNextIterationId is not null)
        {
            var existingPhotos = await GetIterationPhotosByIterationIdAsync(connection, existingNextIterationId.Value, cancellationToken);
            return new IterationSummary(
                existingNextIterationId.Value,
                nextIterationNumber,
                existingPhotos.Count,
                existingPhotos.Count(photo => photo.DecisionType is not null),
                existingPhotos.Count(photo => string.Equals(photo.DecisionType, "choose", StringComparison.OrdinalIgnoreCase)),
                existingPhotos.Count(photo => string.Equals(photo.DecisionType, "ignore", StringComparison.OrdinalIgnoreCase)));
        }

        var chosenPhotos = currentPhotos
            .Where(photo => string.Equals(photo.DecisionType, "choose", StringComparison.OrdinalIgnoreCase))
            .ToList();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var createdAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var nextIterationId = await ExecuteInsertAsync(
            connection,
            transaction,
            """
            INSERT INTO Iterations (ProjectId, Number, CreatedAtUtc)
            VALUES ($projectId, $iterationNumber, $createdAtUtc);
            SELECT last_insert_rowid();
            """,
            cancellationToken,
            ("$projectId", (object)projectId),
            ("$iterationNumber", (object)nextIterationNumber),
            ("$createdAtUtc", (object)createdAtUtc));

        foreach (var photo in chosenPhotos)
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                INSERT INTO MembershipEvents (ProjectId, IterationId, PhotoId, EventType, CreatedAtUtc)
                VALUES ($projectId, $iterationId, $photoId, 'include', $createdAtUtc);
                """,
                cancellationToken,
                ("$projectId", (object)projectId),
                ("$iterationId", (object)nextIterationId),
                ("$photoId", (object)photo.PhotoId),
                ("$createdAtUtc", (object)createdAtUtc));
        }

        await transaction.CommitAsync(cancellationToken);

        return new IterationSummary(nextIterationId, nextIterationNumber, chosenPhotos.Count, 0, 0, 0);
    }

    public async Task PromotePhotoToIterationAsync(
        long projectId,
        long photoId,
        int targetIterationNumber,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var iterationNumbers = await GetIterationNumbersAsync(connection, projectId, cancellationToken);
        if (!iterationNumbers.Contains(targetIterationNumber))
        {
            throw new InvalidOperationException("The target iteration does not exist.");
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var createdAtUtc = DateTimeOffset.UtcNow.ToString("O");

        foreach (var iterationNumber in iterationNumbers.Where(number => number <= targetIterationNumber))
        {
            var iterationId = await TryGetIterationIdAsync(connection, projectId, iterationNumber, cancellationToken);
            if (iterationId is null)
            {
                continue;
            }

            if (await IsPhotoIncludedInIterationAsync(connection, transaction, iterationId.Value, photoId, cancellationToken))
            {
                continue;
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                INSERT INTO MembershipEvents (ProjectId, IterationId, PhotoId, EventType, CreatedAtUtc)
                VALUES ($projectId, $iterationId, $photoId, 'include', $createdAtUtc);
                """,
                cancellationToken,
                ("$projectId", (object)projectId),
                ("$iterationId", (object)iterationId.Value),
                ("$photoId", (object)photoId),
                ("$createdAtUtc", (object)createdAtUtc));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemovePhotoFromIterationAsync(
        long projectId,
        long photoId,
        int startingIterationNumber,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var iterationNumbers = await GetIterationNumbersAsync(connection, projectId, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var createdAtUtc = DateTimeOffset.UtcNow.ToString("O");

        foreach (var iterationNumber in iterationNumbers.Where(number => number >= startingIterationNumber))
        {
            var iterationId = await TryGetIterationIdAsync(connection, projectId, iterationNumber, cancellationToken);
            if (iterationId is null)
            {
                continue;
            }

            if (!await IsPhotoIncludedInIterationAsync(connection, transaction, iterationId.Value, photoId, cancellationToken))
            {
                continue;
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                INSERT INTO MembershipEvents (ProjectId, IterationId, PhotoId, EventType, CreatedAtUtc)
                VALUES ($projectId, $iterationId, $photoId, 'remove', $createdAtUtc);
                """,
                cancellationToken,
                ("$projectId", (object)projectId),
                ("$iterationId", (object)iterationId.Value),
                ("$photoId", (object)photoId),
                ("$createdAtUtc", (object)createdAtUtc));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<long> ExecuteInsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(scalar);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static List<FileInfo> EnumerateSupportedFiles(string folderPath)
    {
        return new DirectoryInfo(folderPath)
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => SupportedExtensions.Contains(file.Extension))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<ImportedProject?> TryGetExistingProjectAsync(
        SqliteConnection connection,
        string folderPath,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.Id, p.FolderPath, COUNT(ph.Id)
            FROM Projects p
            LEFT JOIN Photos ph ON ph.ProjectId = p.Id
            WHERE p.FolderPath = $folderPath
            GROUP BY p.Id, p.FolderPath;
            """;
        command.Parameters.AddWithValue("$folderPath", folderPath);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ImportedProject(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt32(2),
            AlreadyExisted: true);
    }

    private async Task<List<IterationPhoto>> GetIterationPhotosByIterationIdAsync(
        SqliteConnection connection,
        long iterationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH LatestMembership AS (
                SELECT me.PhotoId, me.EventType
                FROM MembershipEvents me
                INNER JOIN (
                    SELECT PhotoId, MAX(Id) AS EventId
                    FROM MembershipEvents
                    WHERE IterationId = $iterationId
                    GROUP BY PhotoId
                ) latestMembership ON latestMembership.EventId = me.Id
            ),
            LatestDecision AS (
                SELECT de.PhotoId, de.DecisionType
                FROM DecisionEvents de
                INNER JOIN (
                    SELECT PhotoId, MAX(Id) AS EventId
                    FROM DecisionEvents
                    WHERE IterationId = $iterationId
                    GROUP BY PhotoId
                ) latestDecision ON latestDecision.EventId = de.Id
            )
            SELECT
                ph.Id,
                ph.FilePath,
                ph.FileName,
                ph.SizeBytes,
                ph.LastModifiedUtc,
                ld.DecisionType
            FROM LatestMembership lm
            INNER JOIN Photos ph ON ph.Id = lm.PhotoId
            LEFT JOIN LatestDecision ld ON ld.PhotoId = ph.Id
            WHERE lm.EventType = 'include'
            ORDER BY ph.SortName, ph.FileName, ph.Id;
            """;
        command.Parameters.AddWithValue("$iterationId", iterationId);

        var photos = new List<IterationPhoto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            photos.Add(new IterationPhoto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) || string.Equals(reader.GetString(5), "undecided", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : reader.GetString(5)));
        }

        return photos;
    }

    private static async Task<long?> TryGetIterationIdAsync(
        SqliteConnection connection,
        long projectId,
        int iterationNumber,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id
            FROM Iterations
            WHERE ProjectId = $projectId AND Number = $iterationNumber;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$iterationNumber", iterationNumber);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is null ? null : Convert.ToInt64(scalar);
    }

    private static async Task<List<int>> GetIterationNumbersAsync(
        SqliteConnection connection,
        long projectId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Number
            FROM Iterations
            WHERE ProjectId = $projectId
            ORDER BY Number;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        var numbers = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            numbers.Add(reader.GetInt32(0));
        }

        return numbers;
    }

    private static async Task<bool> IsPhotoIncludedInIterationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long iterationId,
        long photoId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EventType
            FROM MembershipEvents
            WHERE IterationId = $iterationId AND PhotoId = $photoId
            ORDER BY Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$iterationId", iterationId);
        command.Parameters.AddWithValue("$photoId", photoId);

        var scalar = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return string.Equals(scalar, "include", StringComparison.OrdinalIgnoreCase);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Projects (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FolderPath TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Photos (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                SortName TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                UNIQUE(ProjectId, FilePath),
                FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Iterations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                Number INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UNIQUE(ProjectId, Number),
                FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS MembershipEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                IterationId INTEGER NOT NULL,
                PhotoId INTEGER NOT NULL,
                EventType TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY(IterationId) REFERENCES Iterations(Id) ON DELETE CASCADE,
                FOREIGN KEY(PhotoId) REFERENCES Photos(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS DecisionEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                IterationId INTEGER NOT NULL,
                PhotoId INTEGER NOT NULL,
                DecisionType TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY(IterationId) REFERENCES Iterations(Id) ON DELETE CASCADE,
                FOREIGN KEY(PhotoId) REFERENCES Photos(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ReviewState (
                ProjectId INTEGER PRIMARY KEY,
                IterationId INTEGER NULL,
                PhotoId INTEGER NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY(IterationId) REFERENCES Iterations(Id) ON DELETE CASCADE,
                FOREIGN KEY(PhotoId) REFERENCES Photos(Id) ON DELETE CASCADE
            );
            """;

        _ = command.ExecuteNonQuery();
    }
}
