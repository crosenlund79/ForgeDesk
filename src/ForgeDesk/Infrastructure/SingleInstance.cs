using System.Security.Cryptography;
using System.Text;

namespace ForgeDesk.Infrastructure;

/// <summary>
/// Keeps one ForgeDesk per data directory (two instances would fight over config.json and
/// the browser profile). A second launch asks the first one to come to the front.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly bool _owned;
    private RegisteredWaitHandle? _registration;

    public SingleInstance(string dataDirectory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..16];
        _mutex = new Mutex(initiallyOwned: true, $@"Local\ForgeDesk-{hash}", out _owned);
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\ForgeDesk-{hash}-activate");
    }

    public bool IsFirstInstance => _owned;

    public void SignalFirstInstance() => _activateEvent.Set();

    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
        _activateEvent.Dispose();
    }
}
