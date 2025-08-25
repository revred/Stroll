using Stroll.Dataset;
using Stroll.Storage;

const string Schema = "stroll.history.v1";
const string Version = "1.0.0";

try
{
    var catalog = DataCatalog.Default(Environment.GetEnvironmentVariable("STROLL_DATA"));
    IStorageProvider storage = new CompositeStorage(catalog);
    IPackager pack = new JsonPackager(Schema, Version);

    await Cli.RunAsync(args, storage, catalog, pack);
    Environment.Exit(0);
}
catch (Cli.UsageException u)
{
    Console.Error.WriteLine(JsonPackager.Error("stroll.history.v1", "USAGE", u.Message, u.Hint));
    Environment.Exit(64);
}
catch (Cli.DataException d)
{
    Console.Error.WriteLine(JsonPackager.Error("stroll.history.v1", "DATA", d.Message, d.Hint));
    Environment.Exit(65);
}
catch (Exception ex)
{
    Console.Error.WriteLine(JsonPackager.Error("stroll.history.v1", "INTERNAL", ex.Message, "see logs"));
    Environment.Exit(70);
}
