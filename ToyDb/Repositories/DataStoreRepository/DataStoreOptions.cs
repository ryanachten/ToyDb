namespace ToyDb.Repositories.DataStoreRepository;

public class DataStoreOptions
{
    public static readonly string Key = "DataStore";
    public required string Location { get; set; }
}
