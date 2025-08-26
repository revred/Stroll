using Stroll.Storage;

Console.WriteLine("=== CSV to SQLite Migration ===");

var dataPath = Path.Combine(Environment.CurrentDirectory, "Data");
Console.WriteLine($"Data path: {dataPath}");

if (!Directory.Exists(dataPath))
{
    Console.WriteLine("Data directory not found!");
    return;
}

try
{
    // Initialize SQLite storage
    var catalog = DataCatalog.Default(dataPath);
    using var sqliteStorage = new SqliteStorage(catalog);
    var migrator = new CsvToSqliteMigrator(sqliteStorage);

    // Run migration
    await migrator.MigrateAllCsvFilesAsync(dataPath);

    Console.WriteLine("✅ Migration completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Migration failed: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}