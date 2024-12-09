namespace ToyDb.Repositories;

public interface IDatabaseRepository
{
    string? GetValue(string key);
    Dictionary<string, string?> GetValues();
    string? SetValue(string key, string value);
}