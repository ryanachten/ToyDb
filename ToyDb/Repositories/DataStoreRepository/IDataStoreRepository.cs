﻿using ToyDb.Models;

namespace ToyDb.Repositories.DataStoreRepository;

public interface IDataStoreRepository
{
    long Append(string key, DatabaseEntry entry);
    DatabaseEntry GetValue(long offset);
    Dictionary<string, (DatabaseEntry, long)> GetLatestEntries();
}