namespace ToyDb.Repositories;

public interface IDatabaseRepository
{
    string? GetValue(string key);
    string? SetValue(string key, string value);
}