using System.Security.Cryptography;
using System.Text;

namespace BlazorComponentReadiness.Validator.IO;

public sealed class LibraryRunLock : IDisposable
{
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromSeconds(30);

    private readonly Mutex mutex;
    private bool acquired;

    private LibraryRunLock(Mutex mutex)
    {
        this.mutex = mutex;
        acquired = true;
    }

    public static LibraryRunLock Acquire(string root, string runId)
    {
        var canonicalRoot = Path.GetFullPath(root);
        var key = $"{canonicalRoot}\0{runId}";
        var name = $"blazor-readiness-library-{Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key)))}";
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(AcquisitionTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new DeterministicValidationException(
                    "Timed out waiting for another process to finish this library run transaction.");
            }

            return new LibraryRunLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (acquired)
        {
            mutex.ReleaseMutex();
            acquired = false;
        }

        mutex.Dispose();
    }
}
