namespace NetSpeed.GameBoost.Desktop.Networking;

public sealed record GameConnectionChange(
    DateTime TimestampUtc,
    string ChangeType,
    GameConnectionEndpoint Endpoint)
{
    public string ToDisplayString() =>
        $"{TimestampUtc.ToLocalTime():HH:mm:ss}  {ChangeType,-4}  {Endpoint.Protocol,-4}  {Endpoint.RemoteAddress}  PID {Endpoint.ProcessId}";
}

/// <summary>
/// Compares successive OS connection-table snapshots. It observes metadata only;
/// it does not capture payloads or alter network configuration.
/// </summary>
public sealed class ConnectionChangeTracker
{
    private readonly object _sync = new();
    private HashSet<GameConnectionEndpoint>? _previous;
    private readonly List<GameConnectionChange> _changes = [];
    private const int MaximumChanges = 1000;

    public IReadOnlyList<GameConnectionChange> GetRecentChanges(int count = 100)
    {
        lock (_sync)
            return _changes.TakeLast(Math.Clamp(count, 1, MaximumChanges)).Reverse().ToArray();
    }

    public IReadOnlyList<GameConnectionChange> Update(IEnumerable<GameConnectionEndpoint> snapshot)
    {
        var current = snapshot.ToHashSet();
        lock (_sync)
        {
            if (_previous is null)
            {
                _previous = current;
                return [];
            }

            var now = DateTime.UtcNow;
            var added = current.Except(_previous)
                .Select(endpoint => new GameConnectionChange(now, "新增", endpoint));
            var removed = _previous.Except(current)
                .Select(endpoint => new GameConnectionChange(now, "关闭", endpoint));
            var detected = added.Concat(removed)
                .OrderBy(change => change.ChangeType, StringComparer.Ordinal)
                .ToArray();

            _changes.AddRange(detected);
            if (_changes.Count > MaximumChanges)
                _changes.RemoveRange(0, _changes.Count - MaximumChanges);

            _previous = current;
            return detected;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _previous = null;
            _changes.Clear();
        }
    }
}
