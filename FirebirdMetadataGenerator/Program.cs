using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        //
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"

        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connection-string> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connection-string> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            bool success = BuildDatabase(dbDir, scriptsDir);

                            if (success)
                            {
                                Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                                return 0;
                            }

                            Console.WriteLine("Baza danych NIE została poprawnie zbudowana.");
                            return 2;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            bool success = ExportScripts(connStr, outputDir);

                            if (success)
                            {
                                Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                                return 0;
                            }

                            Console.WriteLine("Eksport zakończył się błędami.");
                            return 2;
                        }

                    case "update-db":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            bool success = UpdateDatabase(connStr, scriptsDir);

                            if (success)
                            {
                                Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                                return 0;
                            }

                            Console.WriteLine("Aktualizacja bazy zakończyła się błędami.");
                            return 2;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                Console.WriteLine(ex.ToString());
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);

            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");

            return args[idx + 1];
        }

        // =====================================================================
        // BUILD DATABASE
        // =====================================================================

        /// <summary>
        /// Buduje nową bazę danych Firebird na podstawie:
        /// 01_domains.sql
        /// 02_tables.sql
        /// 03_procedures.sql
        /// </summary>
        public static bool BuildDatabase(
            string databaseDirectory,
            string scriptsDirectory)
        {
            Directory.CreateDirectory(databaseDirectory);

            string path = Path.Combine(databaseDirectory, "database.fdb");

            var connectionStringBuilder = new FbConnectionStringBuilder
            {
                Database = path,
                UserID = "SYSDBA",
                Password = "masterkey",
                ServerType = FbServerType.Default,
                DataSource = "localhost"
            };

            Console.WriteLine("Tworzę bazę pod ścieżką: " + path);

            if (File.Exists(path))
            {
                Console.WriteLine("Baza już istnieje: " + path);
                Console.WriteLine("Nie nadpisuję istniejącej bazy.");
                return false;
            }

            try
            {
                FbConnection.CreateDatabase(
                    connectionStringBuilder.ConnectionString);

                Console.WriteLine("Baza utworzona: " + path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Błąd przy tworzeniu bazy: " + ex.Message);

                return false;
            }

            if (!Directory.Exists(scriptsDirectory))
            {
                Console.WriteLine(
                    "Katalog skryptów nie istnieje: " +
                    scriptsDirectory);

                return false;
            }

            // Kolejność jest bardzo ważna.
            var sqlFiles = new[]
            {
                "01_domains.sql",
                "02_tables.sql",
                "03_procedures.sql"
            }
            .Select(x => Path.Combine(scriptsDirectory, x))
            .Where(File.Exists)
            .ToList();

            if (sqlFiles.Count == 0)
            {
                Console.WriteLine(
                    "Brak obsługiwanych plików .sql w katalogu: " +
                    scriptsDirectory);

                return false;
            }

            int successFiles = 0;
            int errorFiles = 0;
            int successCommands = 0;
            int errorCommands = 0;

            using (var connection =
                   new FbConnection(
                       connectionStringBuilder.ConnectionString))
            {
                connection.Open();

                Console.WriteLine(
                    "Połączenie otwarte. Wykonuję skrypty...\n");

                foreach (var file in sqlFiles)
                {
                    Console.WriteLine(
                        $"Plik: {Path.GetFileName(file)}");

                    try
                    {
                        string content = File.ReadAllText(file);

                        var commands = SplitSqlCommands(content);

                        int fileErrors = 0;

                        foreach (var cmdText in commands)
                        {
                            string sql = cmdText.Trim();

                            if (string.IsNullOrWhiteSpace(sql))
                                continue;

                            try
                            {
                                ExecuteCommand(connection, sql);

                                successCommands++;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"Błąd polecenia:\n{sql}\n");

                                Console.WriteLine(
                                    ex.Message);

                                Console.WriteLine();

                                errorCommands++;
                                fileErrors++;
                            }
                        }

                        if (fileErrors == 0)
                        {
                            successFiles++;
                            Console.WriteLine(
                                $"Plik wykonany poprawnie ({commands.Count} poleceń).");
                        }
                        else
                        {
                            errorFiles++;

                            Console.WriteLine(
                                $"Plik zakończony błędami. " +
                                $"Polecenia: {commands.Count}, " +
                                $"błędy: {fileErrors}");
                        }

                        Console.WriteLine();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Błąd przy czytaniu pliku: {ex.Message}");

                        errorFiles++;
                    }
                }
            }

            Console.WriteLine("=== Raport budowania ===");
            Console.WriteLine(
                $"Pliki: {successFiles} OK, {errorFiles} błędów");
            Console.WriteLine(
                $"Polecenia: {successCommands} OK, {errorCommands} błędów");

            return errorFiles == 0 &&
                   errorCommands == 0;
        }

        // =====================================================================
        // EXPORT
        // =====================================================================

        /// <summary>
        /// Eksportuje domeny, tabele i procedury.
        /// </summary>
        public static bool ExportScripts(
            string connectionString,
            string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            var domainScripts = new List<string>();
            var tableScripts = new List<string>();
            var procedureScripts = new List<string>();

            bool success = true;

            using (var connection =
                   new FbConnection(connectionString))
            {
                connection.Open();

                Console.WriteLine(
                    "Połączenie otwarte. Pobieranie metadanych...\n");

                try
                {
                    domainScripts =
                        ExportDomains(connection);

                    Console.WriteLine(
                        $"Pobrano domeny: {domainScripts.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Błąd przy pobieraniu domen: {ex.Message}");

                    success = false;
                }

                try
                {
                    tableScripts =
                        ExportTables(connection);

                    Console.WriteLine(
                        $"Pobrano tabele: {tableScripts.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Błąd przy pobieraniu tabel: {ex.Message}");

                    success = false;
                }

                try
                {
                    procedureScripts =
                        ExportProcedures(connection);

                    Console.WriteLine(
                        $"Pobrano procedury: {procedureScripts.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Błąd przy pobieraniu procedur: {ex.Message}");

                    success = false;
                }
            }

            WriteScriptFile(
                Path.Combine(
                    outputDirectory,
                    "01_domains.sql"),
                domainScripts);

            WriteScriptFile(
                Path.Combine(
                    outputDirectory,
                    "02_tables.sql"),
                tableScripts);

            WriteProcedureScriptFile(
                Path.Combine(
                    outputDirectory,
                    "03_procedures.sql"),
                procedureScripts);

            Console.WriteLine(
                $"\nSkrypty wyeksportowane do: {outputDirectory}");

            return success;
        }

        // =====================================================================
        // UPDATE DATABASE
        // =====================================================================

        /// <summary>
        /// Aktualizuje istniejącą bazę różnicowo.
        ///
        /// Obsługiwane:
        /// - CREATE DOMAIN
        /// - CREATE TABLE
        /// - ALTER TABLE ADD
        /// - CREATE/ALTER PROCEDURE
        ///
        /// Celowo nie usuwa obiektów ani kolumn.
        /// </summary>
        public static bool UpdateDatabase(
            string connectionString,
            string scriptsDirectory)
        {
            if (!Directory.Exists(scriptsDirectory))
            {
                Console.WriteLine(
                    "Katalog skryptów nie istnieje: " +
                    scriptsDirectory);

                return false;
            }

            if (!Directory.GetFiles(scriptsDirectory, "*.sql").Any())
            {
                Console.WriteLine("Brak plików .sql w katalogu:");
                Console.WriteLine($"  {scriptsDirectory}");
                return false;
            }

            var sqlFiles = new[]
            {
                "01_domains.sql",
                "02_tables.sql",
                "03_procedures.sql"
            }
            .Select(x => Path.Combine(scriptsDirectory, x))
            .Where(File.Exists)
            .ToList();

            if (sqlFiles.Count == 0)
            {
                Console.WriteLine(
                    "Brak obsługiwanych plików .sql w katalogu: " +
                    scriptsDirectory);

                return false;
            }

            int domainsCreated = 0;
            int domainsSkipped = 0;

            int tablesCreated = 0;
            int columnsAdded = 0;
            int tablesUnchanged = 0;

            int proceduresApplied = 0;
            int errorCommands = 0;

            using (var connection =
                   new FbConnection(connectionString))
            {
                connection.Open();

                Console.WriteLine(
                    "Połączenie otwarte. " +
                    "Wykonuję aktualizację różnicową...\n");

                foreach (var file in sqlFiles)
                {
                    Console.WriteLine(
                        $"Plik: {Path.GetFileName(file)}");

                    string content;

                    try
                    {
                        content = File.ReadAllText(file);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Błąd przy czytaniu pliku: {ex.Message}");

                        errorCommands++;
                        continue;
                    }

                    var commands =
                        SplitSqlCommands(content);

                    foreach (var cmdText in commands)
                    {
                        string sql = cmdText.Trim();

                        if (string.IsNullOrWhiteSpace(sql))
                            continue;

                        try
                        {
                            // -------------------------------------------------
                            // DOMAIN
                            // -------------------------------------------------

                            if (StartsWithSql(
                                sql,
                                "CREATE DOMAIN"))
                            {
                                string domainName =
                                    ExtractObjectName(
                                        sql,
                                        @"CREATE\s+DOMAIN\s+(.+?)\s+AS\b");

                                if (string.IsNullOrWhiteSpace(domainName))
                                {
                                    throw new InvalidOperationException(
                                        "Nie można odczytać nazwy domeny.");
                                }

                                if (DomainExists(
                                    connection,
                                    domainName))
                                {
                                    domainsSkipped++;
                                }
                                else
                                {
                                    ExecuteCommand(
                                        connection,
                                        sql);

                                    domainsCreated++;
                                }

                                continue;
                            }

                            // -------------------------------------------------
                            // TABLE
                            // -------------------------------------------------

                            if (StartsWithSql(
                                sql,
                                "CREATE TABLE"))
                            {
                                var parsed =
                                    ParseCreateTable(sql);

                                string tableName =
                                    parsed.TableName;

                                if (string.IsNullOrWhiteSpace(tableName))
                                {
                                    throw new InvalidOperationException(
                                        "Nie można odczytać nazwy tabeli.");
                                }

                                if (!TableExists(
                                    connection,
                                    tableName))
                                {
                                    ExecuteCommand(
                                        connection,
                                        sql);

                                    tablesCreated++;
                                }
                                else
                                {
                                    var existingColumns =
                                        GetExistingColumns(
                                            connection,
                                            tableName);

                                    bool anyAdded = false;

                                    foreach (
                                        var column
                                        in parsed.Columns)
                                    {
                                        if (string.IsNullOrWhiteSpace(
                                            column.ColumnName))
                                        {
                                            continue;
                                        }

                                        if (!existingColumns.Contains(
                                            column.ColumnName))
                                        {
                                            string alterSql =
                                                $"ALTER TABLE {QuoteIdentifier(tableName)} " +
                                                $"ADD {column.ColumnDefinition}";

                                            ExecuteCommand(
                                                connection,
                                                alterSql);

                                            columnsAdded++;
                                            anyAdded = true;
                                        }
                                    }

                                    if (!anyAdded)
                                        tablesUnchanged++;
                                }

                                continue;
                            }

                            // -------------------------------------------------
                            // PROCEDURE
                            // -------------------------------------------------

                            if (StartsWithSql(
                                sql,
                                "CREATE PROCEDURE"))
                            {
                                string alterSql =
                                    Regex.Replace(
                                        sql,
                                        @"^\s*CREATE\s+PROCEDURE\b",
                                        "CREATE OR ALTER PROCEDURE",
                                        RegexOptions.IgnoreCase);

                                ExecuteCommand(
                                    connection,
                                    alterSql);

                                proceduresApplied++;

                                continue;
                            }

                            // -------------------------------------------------
                            // CREATE OR ALTER PROCEDURE
                            // -------------------------------------------------

                            if (StartsWithSql(
                                sql,
                                "CREATE OR ALTER PROCEDURE"))
                            {
                                ExecuteCommand(
                                    connection,
                                    sql);

                                proceduresApplied++;

                                continue;
                            }

                            // -------------------------------------------------
                            // Pozostałe polecenia
                            // -------------------------------------------------

                            ExecuteCommand(
                                connection,
                                sql);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Błąd polecenia:\n{sql}\n");

                            Console.WriteLine(
                                ex.Message);

                            Console.WriteLine();

                            errorCommands++;
                        }
                    }
                }
            }

            Console.WriteLine(
                "\n=== Raport aktualizacji różnicowej ===");

            Console.WriteLine(
                $"Domeny: {domainsCreated} utworzone, " +
                $"{domainsSkipped} pominięte");

            Console.WriteLine(
                $"Tabele: {tablesCreated} utworzone, " +
                $"{columnsAdded} kolumn dodanych, " +
                $"{tablesUnchanged} bez zmian");

            Console.WriteLine(
                $"Procedury: {proceduresApplied} zastosowane");

            Console.WriteLine(
                $"Błędy poleceń: {errorCommands}");

            return errorCommands == 0;
        }

        // =====================================================================
        // SQL EXECUTION
        // =====================================================================

        private static void ExecuteCommand(
            FbConnection connection,
            string sql)
        {
            using (var command =
                   new FbCommand(sql, connection))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }
        }

        // =====================================================================
        // SQL SPLITTER
        // =====================================================================
        private static List<string> SplitSqlCommands(
            string content)
        {
            var commands = new List<string>();

            if (string.IsNullOrWhiteSpace(content))
                return commands;

            string terminator = ";";

            var current = new StringBuilder();

            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool inLineComment = false;
            bool inBlockComment = false;

            int blockDepth = 0;

            var lines = content.Split(
                new[]
                {
                    "\r\n",
                    "\r",
                    "\n"
                },
                StringSplitOptions.None);

            foreach (string rawLine in lines)
            {
                string trimmed = rawLine.Trim();

                // -------------------------------------------------------------
                // SET TERM
                // -------------------------------------------------------------

                if (!inSingleQuote &&
                    !inDoubleQuote &&
                    !inBlockComment &&
                    Regex.IsMatch(
                        trimmed,
                        @"^SET\s+TERM\b",
                        RegexOptions.IgnoreCase))
                {
                    var parts =
                        trimmed.Split(
                            new[]
                            {
                                ' ',
                                '\t'
                            },
                            StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 3)
                    {
                        terminator = parts[2];
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed) &&
                    current.Length == 0)
                {
                    continue;
                }

                current.AppendLine(rawLine);
                if (!string.Equals(
                    terminator,
                    ";",
                    StringComparison.Ordinal))
                {
                    if (LineEndsWithTerminator(
                        rawLine,
                        terminator))
                    {
                        string command =
                            current.ToString().Trim();

                        command =
                            RemoveTrailingTerminator(
                                command,
                                terminator);

                        if (!string.IsNullOrWhiteSpace(command))
                            commands.Add(command);

                        current.Clear();
                    }

                    continue;
                }

                inLineComment = false;

                for (int i = 0;
                     i < rawLine.Length;
                     i++)
                {
                    char c = rawLine[i];

                    char next =
                        i + 1 < rawLine.Length
                            ? rawLine[i + 1]
                            : '\0';

                    if (inLineComment)
                        break;

                    if (inBlockComment)
                    {
                        if (c == '*' && next == '/')
                        {
                            inBlockComment = false;
                            i++;
                        }

                        continue;
                    }

                    if (!inSingleQuote &&
                        !inDoubleQuote)
                    {
                        if (c == '-' && next == '-')
                        {
                            inLineComment = true;
                            break;
                        }

                        if (c == '/' && next == '*')
                        {
                            inBlockComment = true;
                            i++;
                            continue;
                        }
                    }

                    if (c == '\'' &&
                        !inDoubleQuote)
                    {
                        if (inSingleQuote &&
                            next == '\'')
                        {
                            i++;
                            continue;
                        }

                        inSingleQuote = !inSingleQuote;
                        continue;
                    }

                    if (c == '"' &&
                        !inSingleQuote)
                    {
                        if (inDoubleQuote &&
                            next == '"')
                        {
                            i++;
                            continue;
                        }

                        inDoubleQuote = !inDoubleQuote;
                        continue;
                    }

                    if (inSingleQuote ||
                        inDoubleQuote)
                    {
                        continue;
                    }

                    // BEGIN
                    if (IsKeywordAt(
                        rawLine,
                        i,
                        "BEGIN"))
                    {
                        blockDepth++;
                        i += 4;
                        continue;
                    }

                    // END
                    if (IsKeywordAt(
                        rawLine,
                        i,
                        "END"))
                    {
                        if (blockDepth > 0)
                            blockDepth--;

                        i += 2;
                        continue;
                    }
                }

                if (!inSingleQuote &&
                    !inDoubleQuote &&
                    !inBlockComment &&
                    blockDepth == 0 &&
                    LineEndsWithTerminator(
                        rawLine,
                        ";"))
                {
                    string command =
                        current.ToString().Trim();

                    command =
                        RemoveTrailingTerminator(
                            command,
                            ";");

                    if (!string.IsNullOrWhiteSpace(command))
                        commands.Add(command);

                    current.Clear();
                }
            }

            if (current.Length > 0)
            {
                string command =
                    current.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(command))
                {
                    command =
                        RemoveTrailingTerminator(
                            command,
                            terminator);

                    if (!string.IsNullOrWhiteSpace(command))
                        commands.Add(command);
                }
            }

            return commands;
        }

        private static bool LineEndsWithTerminator(
            string line,
            string terminator)
        {
            return line
                .TrimEnd()
                .EndsWith(
                    terminator,
                    StringComparison.Ordinal);
        }

        private static string RemoveTrailingTerminator(
            string sql,
            string terminator)
        {
            string result =
                sql.TrimEnd();

            if (result.EndsWith(
                terminator,
                StringComparison.Ordinal))
            {
                result =
                    result.Substring(
                        0,
                        result.Length -
                        terminator.Length);
            }

            return result.TrimEnd();
        }

        private static bool IsKeywordAt(
            string text,
            int index,
            string keyword)
        {
            if (index < 0 ||
                index + keyword.Length > text.Length)
            {
                return false;
            }

            if (!text.Substring(
                    index,
                    keyword.Length)
                .Equals(
                    keyword,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool leftOk =
                index == 0 ||
                !IsIdentifierChar(
                    text[index - 1]);

            int end =
                index + keyword.Length;

            bool rightOk =
                end >= text.Length ||
                !IsIdentifierChar(
                    text[end]);

            return leftOk && rightOk;
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) ||
                   c == '_' ||
                   c == '$';
        }

        // =====================================================================
        // DOMAINS
        // =====================================================================

        private static List<string> ExportDomains(
            FbConnection connection)
        {
            var scripts = new List<string>();
            string query = @"
                SELECT
                    f.RDB$FIELD_NAME,
                    f.RDB$FIELD_TYPE,
                    f.RDB$FIELD_LENGTH,
                    f.RDB$FIELD_PRECISION,
                    f.RDB$FIELD_SCALE,
                    f.RDB$FIELD_SUB_TYPE,
                    f.RDB$NULL_FLAG,
                    f.RDB$DEFAULT_SOURCE,
                    f.RDB$VALIDATION_SOURCE,
                    f.RDB$CHARACTER_SET_ID
                FROM RDB$FIELDS f
                WHERE COALESCE(f.RDB$SYSTEM_FLAG, 0) = 0
                  AND f.RDB$FIELD_NAME NOT STARTING WITH 'RDB$'
                ORDER BY f.RDB$FIELD_NAME";

            using (var command =
                   new FbCommand(query, connection))
            using (var reader =
                   command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name =
                        reader["RDB$FIELD_NAME"]
                            ?.ToString()
                            ?.Trim()
                        ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    short fieldType =
                        GetShort(
                            reader["RDB$FIELD_TYPE"]);

                    short fieldLength =
                        GetShort(
                            reader["RDB$FIELD_LENGTH"]);

                    short precision =
                        GetShort(
                            reader["RDB$FIELD_PRECISION"]);

                    short scale =
                        GetShort(
                            reader["RDB$FIELD_SCALE"]);

                    short subType =
                        GetShort(
                            reader["RDB$FIELD_SUB_TYPE"]);

                    string type =
                        GetFieldType(
                            fieldType,
                            fieldLength,
                            precision,
                            scale,
                            subType);

                    var sb =
                        new StringBuilder();

                    sb.Append(
                        $"CREATE DOMAIN {QuoteIdentifier(name)} AS {type}");

                    // NULL / NOT NULL
                    if (reader["RDB$NULL_FLAG"] != DBNull.Value &&
                        Convert.ToInt32(
                            reader["RDB$NULL_FLAG"]) == 1)
                    {
                        sb.Append(" NOT NULL");
                    }

                    // DEFAULT
                    string defaultSource =
                        reader["RDB$DEFAULT_SOURCE"] == DBNull.Value
                            ? string.Empty
                            : reader["RDB$DEFAULT_SOURCE"]
                                ?.ToString()
                                ?.Trim()
                              ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(
                        defaultSource))
                    {
                        sb.Append(" ");
                        sb.Append(
                            NormalizeDefaultSource(
                                defaultSource));
                    }

                    sb.Append(";");

                    scripts.Add(
                        sb.ToString());
                }
            }

            return scripts;
        }

        private static string NormalizeDefaultSource(
            string source)
        {
            return source.Trim();
        }

        // =====================================================================
        // TABLES
        // =====================================================================

        private static List<string> ExportTables(
            FbConnection connection)
        {
            var scripts = new List<string>();

            string query = @"
                SELECT
                    r.RDB$RELATION_NAME
                FROM RDB$RELATIONS r
                WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
                  AND r.RDB$VIEW_BLR IS NULL
                ORDER BY r.RDB$RELATION_NAME";

            using (var command =
                   new FbCommand(query, connection))
            using (var reader =
                   command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string tableName =
                        reader["RDB$RELATION_NAME"]
                            ?.ToString()
                            ?.Trim()
                        ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(
                        tableName))
                    {
                        continue;
                    }

                    string tableScript =
                        ExportTableDefinition(
                            connection,
                            tableName);

                    if (!string.IsNullOrWhiteSpace(
                        tableScript))
                    {
                        scripts.Add(tableScript);
                    }
                }
            }

            return scripts;
        }

        private static string ExportTableDefinition(
            FbConnection connection,
            string tableName)
        {
            var columns =
                new List<string>();

            string query = @"
                SELECT
                    rf.RDB$FIELD_NAME,
                    rf.RDB$FIELD_SOURCE,
                    rf.RDB$NULL_FLAG,

                    f.RDB$FIELD_TYPE,
                    f.RDB$FIELD_LENGTH,
                    f.RDB$FIELD_PRECISION,
                    f.RDB$FIELD_SCALE,
                    f.RDB$FIELD_SUB_TYPE,
                    f.RDB$CHARACTER_SET_ID

                FROM RDB$RELATION_FIELDS rf

                JOIN RDB$FIELDS f
                  ON f.RDB$FIELD_NAME =
                     rf.RDB$FIELD_SOURCE

                WHERE rf.RDB$RELATION_NAME =
                      @tableName

                ORDER BY
                    rf.RDB$FIELD_POSITION";

            using (var command =
                   new FbCommand(query, connection))
            {
                command.Parameters.AddWithValue(
                    "@tableName",
                    tableName);

                using (var reader =
                       command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columnName =
                            reader["RDB$FIELD_NAME"]
                                ?.ToString()
                                ?.Trim()
                            ?? string.Empty;

                        string fieldSource =
                            reader["RDB$FIELD_SOURCE"]
                                ?.ToString()
                                ?.Trim()
                            ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(
                            columnName))
                        {
                            continue;
                        }

                        string fieldType;

                        /*
                         * Jeżeli kolumna korzysta z domeny użytkownika,
                         * np.:
                         *
                         * D_NAME
                         *
                         * to zachowujemy nazwę domeny.
                         *
                         * Jeżeli jest to implicit RDB$...,
                         * odtwarzamy fizyczny typ.
                         */
                        if (!string.IsNullOrWhiteSpace(
                                fieldSource) &&
                            !fieldSource.StartsWith(
                                "RDB$",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            fieldType =
                                QuoteIdentifier(
                                    fieldSource);
                        }
                        else
                        {
                            fieldType =
                                GetFieldType(
                                    GetShort(
                                        reader["RDB$FIELD_TYPE"]),
                                    GetShort(
                                        reader["RDB$FIELD_LENGTH"]),
                                    GetShort(
                                        reader["RDB$FIELD_PRECISION"]),
                                    GetShort(
                                        reader["RDB$FIELD_SCALE"]),
                                    GetShort(
                                        reader["RDB$FIELD_SUB_TYPE"]));
                        }

                        string nullPart = "";

                        if (reader["RDB$NULL_FLAG"] != DBNull.Value &&
                            Convert.ToInt32(
                                reader["RDB$NULL_FLAG"]) == 1)
                        {
                            nullPart = " NOT NULL";
                        }

                        columns.Add(
                            $"  {QuoteIdentifier(columnName)} " +
                            $"{fieldType}{nullPart}");
                    }
                }
            }

            if (columns.Count == 0)
                return string.Empty;

            var sb =
                new StringBuilder();

            sb.AppendLine(
                $"CREATE TABLE {QuoteIdentifier(tableName)} (");

            sb.AppendLine(
                string.Join(
                    ",\n",
                    columns));

            sb.AppendLine(");");

            return sb.ToString();
        }

        // =====================================================================
        // PROCEDURES
        // =====================================================================

        private static List<string> ExportProcedures(
            FbConnection connection)
        {
            var scripts =
                new List<string>();

            string query = @"
                SELECT
                    p.RDB$PROCEDURE_NAME,
                    p.RDB$PROCEDURE_SOURCE
                FROM RDB$PROCEDURES p
                WHERE COALESCE(p.RDB$SYSTEM_FLAG, 0) = 0
                ORDER BY p.RDB$PROCEDURE_NAME";

            var procedures =
                new List<(string Name, string Source)>();

            using (var command =
                   new FbCommand(query, connection))
            using (var reader =
                   command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name =
                        reader["RDB$PROCEDURE_NAME"]
                            ?.ToString()
                            ?.Trim()
                        ?? string.Empty;

                    string source =
                        reader["RDB$PROCEDURE_SOURCE"] ==
                        DBNull.Value
                            ? string.Empty
                            : reader["RDB$PROCEDURE_SOURCE"]
                                ?.ToString()
                              ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        procedures.Add(
                            (name, source));
                    }
                }
            }

            foreach (var procedure in procedures)
            {
                var inputParams =
                    GetProcedureParameters(
                        connection,
                        procedure.Name,
                        0);

                var outputParams =
                    GetProcedureParameters(
                        connection,
                        procedure.Name,
                        1);

                var sb =
                    new StringBuilder();

                sb.Append(
                    $"CREATE PROCEDURE " +
                    $"{QuoteIdentifier(procedure.Name)}");

                // INPUT
                if (inputParams.Count > 0)
                {
                    sb.AppendLine(" (");
                    sb.AppendLine(
                        string.Join(
                            ",\n",
                            inputParams.Select(
                                x => "  " + x)));

                    sb.AppendLine(")");
                }
                else
                {
                    sb.AppendLine();
                }

                // OUTPUT
                if (outputParams.Count > 0)
                {
                    sb.AppendLine("RETURNS (");

                    sb.AppendLine(
                        string.Join(
                            ",\n",
                            outputParams.Select(
                                x => "  " + x)));

                    sb.AppendLine(")");
                }

                sb.AppendLine("AS");

                string source =
                    procedure.Source.Trim();

                if (string.IsNullOrWhiteSpace(
                    source))
                {
                    source =
                        "BEGIN\nEND";
                }

                sb.AppendLine(source);

                // SET TERM ^ ;
                sb.Append("^");

                scripts.Add(
                    sb.ToString());
            }

            return scripts;
        }

        private static List<string> GetProcedureParameters(
            FbConnection connection,
            string procedureName,
            short parameterType)
        {
            var result =
                new List<string>();

            string query = @"
                SELECT
                    pp.RDB$PARAMETER_NAME,
                    pp.RDB$FIELD_SOURCE,

                    f.RDB$FIELD_TYPE,
                    f.RDB$FIELD_LENGTH,
                    f.RDB$FIELD_PRECISION,
                    f.RDB$FIELD_SCALE,
                    f.RDB$FIELD_SUB_TYPE

                FROM RDB$PROCEDURE_PARAMETERS pp

                JOIN RDB$FIELDS f
                  ON f.RDB$FIELD_NAME =
                     pp.RDB$FIELD_SOURCE

                WHERE pp.RDB$PROCEDURE_NAME =
                      @procName

                  AND pp.RDB$PARAMETER_TYPE =
                      @paramType

                ORDER BY
                    pp.RDB$PARAMETER_NUMBER";

            using (var command =
                   new FbCommand(query, connection))
            {
                command.Parameters.AddWithValue(
                    "@procName",
                    procedureName);

                command.Parameters.AddWithValue(
                    "@paramType",
                    parameterType);

                using (var reader =
                       command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string parameterName =
                            reader["RDB$PARAMETER_NAME"]
                                ?.ToString()
                                ?.Trim()
                            ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(
                            parameterName))
                        {
                            continue;
                        }

                        string fieldSource =
                            reader["RDB$FIELD_SOURCE"]
                                ?.ToString()
                                ?.Trim()
                            ?? string.Empty;

                        string type;

                        if (!string.IsNullOrWhiteSpace(
                                fieldSource) &&
                            !fieldSource.StartsWith(
                                "RDB$",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            type =
                                QuoteIdentifier(
                                    fieldSource);
                        }
                        else
                        {
                            type =
                                GetFieldType(
                                    GetShort(
                                        reader["RDB$FIELD_TYPE"]),
                                    GetShort(
                                        reader["RDB$FIELD_LENGTH"]),
                                    GetShort(
                                        reader["RDB$FIELD_PRECISION"]),
                                    GetShort(
                                        reader["RDB$FIELD_SCALE"]),
                                    GetShort(
                                        reader["RDB$FIELD_SUB_TYPE"]));
                        }

                        result.Add(
                            $"{QuoteIdentifier(parameterName)} {type}");
                    }
                }
            }

            return result;
        }

        // =====================================================================
        // FIREBIRD TYPES
        // =====================================================================

        private static string GetFieldType(
            short fieldType,
            short fieldLength,
            short precision,
            short scale,
            short subType)
        {
            switch (fieldType)
            {
                case 7:
                    return scale == 0
                        ? "SMALLINT"
                        : $"NUMERIC({precision},{Math.Abs(scale)})";

                case 8:
                    return scale == 0
                        ? "INTEGER"
                        : $"NUMERIC({precision},{Math.Abs(scale)})";

                case 16:
                    return scale == 0
                        ? "BIGINT"
                        : $"NUMERIC({precision},{Math.Abs(scale)})";

                case 10:
                    return "FLOAT";

                case 27:
                    return "DOUBLE PRECISION";

                case 12:
                    return "DATE";

                case 13:
                    return "TIME";

                case 14:
                    return fieldLength > 0
                        ? $"CHAR({fieldLength})"
                        : "CHAR(1)";

                case 37:
                    return fieldLength > 0
                        ? $"VARCHAR({fieldLength})"
                        : "VARCHAR(255)";

                case 40:
                    return fieldLength > 0
                        ? $"CSTRING({fieldLength})"
                        : "CSTRING(255)";

                case 23:
                    return "BOOLEAN";

                case 35:
                    return "TIMESTAMP";

                case 261:
                    return subType == 0
                        ? "BLOB SUB_TYPE 0"
                        : "BLOB SUB_TYPE 1";

                /*
                 * Firebird 4/5:
                 *
                 * 16 + scale/precision może również reprezentować
                 * INT128/NUMERIC/DECIMAL.
                 *
                 * Dla typowego eksportu domen używamy NUMERIC,
                 * gdy obecne są precision/scale.
                 */

                default:
                    return "VARCHAR(255)";
            }
        }

        private static short GetShort(
            object value)
        {
            if (value == null ||
                value == DBNull.Value)
            {
                return 0;
            }

            return Convert.ToInt16(value);
        }

        // =====================================================================
        // FILE WRITERS
        // =====================================================================

        private static void WriteScriptFile(
            string filePath,
            List<string> scripts)
        {
            if (scripts.Count == 0)
            {
                File.WriteAllText(
                    filePath,
                    "-- Brak obiektów tego typu\n");
            }
            else
            {
                string content =
                    string.Join(
                        "\n\n",
                        scripts);

                content += "\n";

                File.WriteAllText(
                    filePath,
                    content);
            }

            Console.WriteLine(
                $"Zapisano: {Path.GetFileName(filePath)}");
        }

        private static void WriteProcedureScriptFile(
            string filePath,
            List<string> procedureScripts)
        {
            if (procedureScripts.Count == 0)
            {
                File.WriteAllText(
                    filePath,
                    "-- Brak obiektów tego typu\n");
            }
            else
            {
                var sb =
                    new StringBuilder();

                sb.AppendLine(
                    "SET TERM ^ ;");

                sb.AppendLine();

                sb.AppendLine(
                    string.Join(
                        "\n\n",
                        procedureScripts));

                sb.AppendLine();

                sb.AppendLine(
                    "SET TERM ; ^");

                File.WriteAllText(
                    filePath,
                    sb.ToString());
            }

            Console.WriteLine(
                $"Zapisano: {Path.GetFileName(filePath)}");
        }

        // =====================================================================
        // EXISTENCE CHECKS
        // =====================================================================

        private static bool DomainExists(
            FbConnection connection,
            string domainName)
        {
            string normalized =
                UnquoteIdentifier(
                    domainName)
                .ToUpperInvariant();

            if (normalized.StartsWith("RDB$"))
                return false;

            string query = @"
                SELECT 1
                FROM RDB$FIELDS
                WHERE RDB$FIELD_NAME = @name
                  AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0";

            using (var command =
                   new FbCommand(query, connection))
            {
                command.Parameters.AddWithValue(
                    "@name",
                    normalized);

                using (var reader =
                       command.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }

        private static bool TableExists(
            FbConnection connection,
            string tableName)
        {
            string normalized =
                UnquoteIdentifier(
                    tableName)
                .ToUpperInvariant();

            string query = @"
                SELECT 1
                FROM RDB$RELATIONS
                WHERE RDB$RELATION_NAME = @name
                  AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
                  AND RDB$VIEW_BLR IS NULL";

            using (var command =
                   new FbCommand(query, connection))
            {
                command.Parameters.AddWithValue(
                    "@name",
                    normalized);

                using (var reader =
                       command.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }

        private static HashSet<string> GetExistingColumns(
            FbConnection connection,
            string tableName)
        {
            var result =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            string normalized =
                UnquoteIdentifier(
                    tableName)
                .ToUpperInvariant();

            string query = @"
                SELECT
                    RDB$FIELD_NAME
                FROM RDB$RELATION_FIELDS
                WHERE RDB$RELATION_NAME = @name";

            using (var command =
                   new FbCommand(query, connection))
            {
                command.Parameters.AddWithValue(
                    "@name",
                    normalized);

                using (var reader =
                       command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string column =
                            reader["RDB$FIELD_NAME"]
                                ?.ToString()
                                ?.Trim();

                        if (!string.IsNullOrWhiteSpace(
                            column))
                        {
                            result.Add(column);
                        }
                    }
                }
            }

            return result;
        }

        // =====================================================================
        // CREATE TABLE PARSER
        // =====================================================================

        private static (
            string TableName,
            List<(
                string ColumnName,
                string ColumnDefinition)> Columns)
            ParseCreateTable(
                string script)
        {
            var empty =
                new List<(
                    string,
                    string)>();

            var match =
                Regex.Match(
                    script,
                    @"CREATE\s+TABLE\s+(?:" +
                    @"IF\s+NOT\s+EXISTS\s+)?" +
                    @"((?:" +
                    @"""[^""]+""" +
                    @"|[A-Za-z_][A-Za-z0-9_$]*" +
                    @"))\s*\(",
                    RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return (
                    string.Empty,
                    empty);
            }

            string tableName =
                match.Groups[1]
                    .Value
                    .Trim();

            int startIndex =
                script.IndexOf(
                    '(',
                    match.Index +
                    match.Length -
                    1);

            if (startIndex < 0)
            {
                return (
                    tableName,
                    empty);
            }

            int endIndex =
                FindMatchingParenthesis(
                    script,
                    startIndex);

            if (endIndex < 0)
            {
                return (
                    tableName,
                    empty);
            }

            string columnsBlock =
                script.Substring(
                    startIndex + 1,
                    endIndex -
                    startIndex -
                    1);

            var rawColumns =
                SplitTopLevel(
                    columnsBlock,
                    ',');

            var columns =
                new List<(
                    string,
                    string)>();

            foreach (string raw in rawColumns)
            {
                string trimmed =
                    raw.Trim();

                if (string.IsNullOrWhiteSpace(
                    trimmed))
                {
                    continue;
                }

                /*
                 * Eksporter tworzy wyłącznie kolumny,
                 * więc pierwszym elementem jest nazwa kolumny.
                 *
                 * Nie traktujemy tutaj CONSTRAINT jako kolumny.
                 */
                if (Regex.IsMatch(
                    trimmed,
                    @"^(CONSTRAINT|PRIMARY\s+KEY|FOREIGN\s+KEY|UNIQUE|CHECK)\b",
                    RegexOptions.IgnoreCase))
                {
                    continue;
                }

                var nameMatch =
                    Regex.Match(
                        trimmed,
                        @"^(?:" +
                        @"""[^""]+""" +
                        @"|[A-Za-z_][A-Za-z0-9_$]*" +
                        @")");

                if (!nameMatch.Success)
                    continue;

                string columnName =
                    nameMatch.Value;

                columns.Add(
                    (
                        columnName,
                        trimmed));
            }

            return (
                tableName,
                columns);
        }

        private static int FindMatchingParenthesis(
            string text,
            int startIndex)
        {
            int depth = 0;

            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            for (int i = startIndex;
                 i < text.Length;
                 i++)
            {
                char c =
                    text[i];

                char next =
                    i + 1 < text.Length
                        ? text[i + 1]
                        : '\0';

                if (c == '\'' &&
                    !inDoubleQuote)
                {
                    if (inSingleQuote &&
                        next == '\'')
                    {
                        i++;
                        continue;
                    }

                    inSingleQuote =
                        !inSingleQuote;

                    continue;
                }

                if (c == '"' &&
                    !inSingleQuote)
                {
                    if (inDoubleQuote &&
                        next == '"')
                    {
                        i++;
                        continue;
                    }

                    inDoubleQuote =
                        !inDoubleQuote;

                    continue;
                }

                if (inSingleQuote ||
                    inDoubleQuote)
                {
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;

                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static List<string> SplitTopLevel(
            string input,
            char separator)
        {
            var result =
                new List<string>();

            var current =
                new StringBuilder();

            int depth = 0;

            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            for (int i = 0;
                 i < input.Length;
                 i++)
            {
                char ch =
                    input[i];

                char next =
                    i + 1 < input.Length
                        ? input[i + 1]
                        : '\0';

                if (ch == '\'' &&
                    !inDoubleQuote)
                {
                    if (inSingleQuote &&
                        next == '\'')
                    {
                        current.Append(ch);
                        current.Append(next);
                        i++;
                        continue;
                    }

                    inSingleQuote =
                        !inSingleQuote;

                    current.Append(ch);
                    continue;
                }

                if (ch == '"' &&
                    !inSingleQuote)
                {
                    if (inDoubleQuote &&
                        next == '"')
                    {
                        current.Append(ch);
                        current.Append(next);
                        i++;
                        continue;
                    }

                    inDoubleQuote =
                        !inDoubleQuote;

                    current.Append(ch);
                    continue;
                }

                if (!inSingleQuote &&
                    !inDoubleQuote)
                {
                    if (ch == '(')
                        depth++;

                    if (ch == ')')
                        depth--;

                    if (ch == separator &&
                        depth == 0)
                    {
                        result.Add(
                            current.ToString());

                        current.Clear();

                        continue;
                    }
                }

                current.Append(ch);
            }

            if (current.Length > 0)
            {
                result.Add(
                    current.ToString());
            }

            return result;
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private static bool StartsWithSql(
            string sql,
            string keyword)
        {
            return sql.TrimStart()
                .StartsWith(
                    keyword,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractObjectName(
            string sql,
            string pattern)
        {
            var match =
                Regex.Match(
                    sql,
                    pattern,
                    RegexOptions.IgnoreCase);

            if (!match.Success)
                return string.Empty;

            return match.Groups[1]
                .Value
                .Trim()
                .TrimEnd(';');
        }

        private static string QuoteIdentifier(
            string identifier)
        {
            if (string.IsNullOrWhiteSpace(
                identifier))
            {
                return identifier;
            }

            string value =
                identifier.Trim();

            if (value.StartsWith("\"") &&
                value.EndsWith("\""))
            {
                return value;
            }

            /*
             * Dla zwykłych nazw Firebird nie wymaga
             * cudzysłowów. Zachowujemy więc czytelny SQL.
             */
            if (Regex.IsMatch(
                value,
                @"^[A-Za-z_][A-Za-z0-9_$]*$"))
            {
                return value;
            }

            return "\"" +
                   value.Replace(
                       "\"",
                       "\"\"") +
                   "\"";
        }

        private static string UnquoteIdentifier(
            string identifier)
        {
            if (string.IsNullOrWhiteSpace(
                identifier))
            {
                return string.Empty;
            }

            string value =
                identifier.Trim();

            if (value.Length >= 2 &&
                value[0] == '"' &&
                value[value.Length - 1] == '"')
            {
                value =
                    value.Substring(
                        1,
                        value.Length - 2);

                value =
                    value.Replace(
                        "\"\"",
                        "\"");
            }

            return value;
        }
    }
}