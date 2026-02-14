using System;
using System.Collections.Generic;

namespace Contoso.Networking;

// --- Deliberate violations for testing ---

// Critical: Mutable struct with reference-type field and side effects
public struct ConnectionInfo
{
    public string Host { get; set; }
    public int Port { get; set; }
    public List<string> Tags { get; set; }

    public void Connect()
    {
        // Side-effecting method on a value type
    }
}

// Warning: Unsealed leaf class with no virtual members
public class DataProcessor
{
    // Warning: Method named with noun instead of verb
    public string Result(byte[] data)
    {
        if (data == null)
            // Warning: Missing paramName on ArgumentNullException
            throw new ArgumentNullException();

        return Convert.ToBase64String(data);
    }

    // Critical: List<T> return type in public API
    public List<string> GetItems()
    {
        return new List<string>();
    }

    // Suggestion: Property that does expensive work (should be a method)
    public byte[] Checksum
    {
        get
        {
            // Expensive computation — violates property contract
            System.Threading.Thread.Sleep(100);
            return System.Security.Cryptography.SHA256.HashData(Array.Empty<byte>());
        }
    }
}

// --- Things done well (strengths) ---

// Correct event pattern
public class FileWatcher : IDisposable
{
    private bool _disposed;

    public event EventHandler<FileChangedEventArgs>? Changed;

    protected virtual void OnChanged(FileChangedEventArgs e)
    {
        Changed?.Invoke(this, e);
    }

    // Correct IDisposable pattern
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing) { /* release managed resources */ }
            _disposed = true;
        }
    }
}

public class FileChangedEventArgs : EventArgs
{
    public string FilePath { get; }
    public FileChangedEventArgs(string filePath) => FilePath = filePath;
}
