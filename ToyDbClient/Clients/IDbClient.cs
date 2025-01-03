﻿
namespace ToyDbClient.Clients;

internal interface IDbClient
{
    Task DeleteValue(string key);
    Task<T> GetValue<T>(string key);
    Task<Dictionary<string, string>> PrintAllValues();
    Task<T> SetValue<T>(string key, T value);
}