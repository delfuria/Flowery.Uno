using System.Diagnostics.CodeAnalysis;
using Flowery.Controls.Scheduler.Contracts;

namespace Flowery.Controls.Scheduler.Utilities;

/// <summary>Drag data for dropping an external item onto the scheduler.</summary>
/// <remarks>A WinUI data package carries WinRT values, so it holds a token and the item stays in this process.
/// Only the most recent item is kept, because one drag runs at a time; a drop on a scheduler releases it.</remarks>
public static class ExternalDropHandler
{
    public const string Format = "Flowery.Scheduler.Appointment";

    private static (string Token, ISchedulerDroppable Item)? _current;

    public static DataPackage CreateDataPackage(ISchedulerDroppable appointment)
    {
        var data = new DataPackage();
        SetData(data, appointment);
        return data;
    }

    /// <summary>Adds the item to an existing package, such as <c>DragStartingEventArgs.Data</c>.</summary>
    public static void SetData(DataPackage data, ISchedulerDroppable appointment)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(appointment);
        var token = Guid.NewGuid().ToString("N");
        _current = (token, appointment);
        data.Properties[Format] = token;
        data.RequestedOperation |= DataPackageOperation.Copy;
    }

    /// <summary>Forgets the item once a scheduler took it, so the host object is not kept alive.</summary>
    internal static void Release(ISchedulerDroppable item)
    {
        if (_current is { } current && ReferenceEquals(current.Item, item))
            _current = null;
    }

    public static bool TryGetItem(DataPackageView data, [NotNullWhen(true)] out ISchedulerDroppable? item)
    {
        item = _current is { } current && data.Properties.TryGetValue(Format, out var token) && Equals(token, current.Token)
            ? current.Item : null;
        return item is not null;
    }
}
