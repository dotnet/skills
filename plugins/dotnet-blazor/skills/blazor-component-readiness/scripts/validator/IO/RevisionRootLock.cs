using System.Security.Cryptography;
using System.Text;

namespace BlazorComponentReadiness.Validator.IO;

public sealed class RevisionRootLock : IDisposable
{
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromSeconds(30);

    private readonly Mutex mutex;
    private bool acquired;

    private RevisionRootLock(Mutex mutex, bool acquired)
    {
        this.mutex = mutex;
        this.acquired = acquired;
    }

    public static RevisionRootLock Acquire(string revisionRoot)
    {
        var normalizedRoot = Path.GetFullPath(revisionRoot);
        var name = $"blazor-readiness-revisions-{Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))}";
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
                    "Timed out waiting for another writer to finish publishing this revision root.");
            }

            return new RevisionRootLock(mutex, acquired: true);
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
