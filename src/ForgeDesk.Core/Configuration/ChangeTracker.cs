using System.Collections.Specialized;
using System.ComponentModel;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Configuration;

/// <summary>
/// Watches the whole configuration graph and raises <see cref="Changed"/> whenever anything
/// that is persisted changes, so the app can autosave without every edit path remembering to.
/// </summary>
public sealed class ChangeTracker : IDisposable
{
    private readonly AppConfig _config;
    private readonly HashSet<INotifyPropertyChanged> _objects = [];
    private readonly HashSet<INotifyCollectionChanged> _collections = [];

    public ChangeTracker(AppConfig config)
    {
        _config = config;
        Attach(config);
        Attach(config.Preferences);
        AttachProjects(config.Projects);
    }

    public event EventHandler? Changed;

    /// <summary>Number of change notifications raised so far (useful for diagnostics and tests).</summary>
    public int ChangeCount { get; private set; }

    public void Dispose()
    {
        foreach (var o in _objects)
        {
            o.PropertyChanged -= OnPropertyChanged;
        }

        foreach (var c in _collections)
        {
            c.CollectionChanged -= OnCollectionChanged;
        }

        _objects.Clear();
        _collections.Clear();
    }

    private void AttachProjects(IEnumerable<Project> projects)
    {
        AttachCollection(_config.Projects);
        foreach (var project in projects)
        {
            AttachProject(project);
        }
    }

    private void AttachProject(Project project)
    {
        Attach(project);
        Attach(project.Desk);
        foreach (var provider in ProviderInfo.All)
        {
            var list = project.Conversations(provider);
            AttachCollection(list);
            foreach (var mapping in list)
            {
                Attach(mapping);
            }
        }
    }

    private void Attach(INotifyPropertyChanged item)
    {
        if (_objects.Add(item))
        {
            item.PropertyChanged += OnPropertyChanged;
        }
    }

    private void AttachCollection(INotifyCollectionChanged collection)
    {
        if (_collections.Add(collection))
        {
            collection.CollectionChanged += OnCollectionChanged;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => Raise();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                switch (item)
                {
                    case Project project:
                        AttachProject(project);
                        break;
                    case ConversationMapping mapping:
                        Attach(mapping);
                        break;
                }
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Re-walk everything; detached items simply stop mattering.
            AttachProjects(_config.Projects);
        }

        Raise();
    }

    private void Raise()
    {
        ChangeCount++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
