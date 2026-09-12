using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace PharmaERP.Desktop.Common;

public class UiDataChangeBus : IUiDataChangeBus
{
    private readonly List<Action<UiDataChangeEvent>> _subscribers = new();
    private readonly object _lock = new();
    private readonly ILogger<UiDataChangeBus> _logger;

    public UiDataChangeBus(ILogger<UiDataChangeBus> logger)
    {
        _logger = logger;
    }

    public void Publish(UiDataChangeType changeType, object? payload = null)
    {
        var evt = new UiDataChangeEvent(changeType, payload);
        List<Action<UiDataChangeEvent>> snapshot;
        lock (_lock)
        {
            snapshot = new List<Action<UiDataChangeEvent>>(_subscribers);
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler(evt);
            }
            catch (Exception ex)
            {
                // Subscriber isolation: one failed handler must not prevent others.
                // But we do NOT silently discard — log with full context.
                _logger.LogError(
                    ex,
                    "UiDataChangeBus: subscriber threw an unhandled exception while handling {ChangeType}. " +
                    "Handler: {HandlerMethod}. This is a background invalidation failure and will NOT be shown to the user.",
                    changeType,
                    handler.Method.Name);
            }
        }
    }

    public IDisposable Subscribe(Action<UiDataChangeType> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe(evt => handler(evt.ChangeType));
    }

    public IDisposable Subscribe(Action<UiDataChangeEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            _subscribers.Add(handler);
        }

        return new Unsubscriber(this, handler);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly UiDataChangeBus _bus;
        private readonly Action<UiDataChangeEvent> _handler;
        private bool _disposed;

        public Unsubscriber(UiDataChangeBus bus, Action<UiDataChangeEvent> handler)
        {
            _bus = bus;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_bus._lock)
            {
                _bus._subscribers.Remove(_handler);
            }
        }
    }
}


