using System;
using System.Threading;

namespace OrbModding.Common.Runtime;

public enum DiagnosticsBundleState
{
    Unavailable = 0,
    Ready = 1,
    Written = 2,
    WrittenRevealUnavailable = 3,
    Failed = 4,
}

public enum DiagnosticsBundleRequestResult
{
    Accepted = 0,
    Unavailable = 1,
    RequestPending = 2,
}

/// <summary>The result of the last player-requested diagnostics bundle.</summary>
public readonly struct DiagnosticsBundleStatus : IEquatable<DiagnosticsBundleStatus>
{
    public DiagnosticsBundleStatus(
        DiagnosticsBundleState state,
        string path,
        long bytesWritten,
        string failureReason = "")
    {
        if (state is < DiagnosticsBundleState.Unavailable or > DiagnosticsBundleState.Failed)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (bytesWritten < 0) throw new ArgumentOutOfRangeException(nameof(bytesWritten));
        path ??= string.Empty;
        failureReason ??= string.Empty;
        var written = state is DiagnosticsBundleState.Written or
            DiagnosticsBundleState.WrittenRevealUnavailable;
        if (written != (path.Length != 0 && bytesWritten > 0) ||
            (state == DiagnosticsBundleState.Failed) != (failureReason.Length != 0) ||
            !written && path.Length != 0 || !written && bytesWritten != 0)
        {
            throw new ArgumentException("The diagnostics-bundle status fields are inconsistent.", nameof(state));
        }

        State = state;
        Path = path;
        BytesWritten = bytesWritten;
        FailureReason = failureReason;
    }

    public static DiagnosticsBundleStatus Unavailable => new(
        DiagnosticsBundleState.Unavailable, string.Empty, 0);

    public static DiagnosticsBundleStatus Ready => new(
        DiagnosticsBundleState.Ready, string.Empty, 0);

    public DiagnosticsBundleState State { get; }
    public string Path { get; }
    public long BytesWritten { get; }
    public string FailureReason { get; }

    public bool Equals(DiagnosticsBundleStatus other) =>
        State == other.State && BytesWritten == other.BytesWritten &&
        string.Equals(Path, other.Path, StringComparison.Ordinal) &&
        string.Equals(FailureReason, other.FailureReason, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DiagnosticsBundleStatus other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(State, Path, BytesWritten, FailureReason);
    public static bool operator ==(DiagnosticsBundleStatus left, DiagnosticsBundleStatus right) => left.Equals(right);
    public static bool operator !=(DiagnosticsBundleStatus left, DiagnosticsBundleStatus right) => !left.Equals(right);
}

public interface IDiagnosticsBundleControl
{
    DiagnosticsBundleStatus Status { get; }
    bool BundleRequested { get; }
    long Revision { get; }
    DiagnosticsBundleRequestResult RequestBundle();
}

public sealed class DiagnosticsBundleRegistry : IDiagnosticsBundleControl
{
    private readonly int _ownerThreadId;
    private DiagnosticsBundleRegistration? _registration;
    private DiagnosticsBundleStatus _status = DiagnosticsBundleStatus.Unavailable;
    private bool _bundleRequested;
    private bool _requestClaimed;
    private long _revision;

    public DiagnosticsBundleRegistry() => _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    public static DiagnosticsBundleRegistry Shared { get; } = new();

    public DiagnosticsBundleStatus Status
    {
        get { AssertOwnerThread(); return _status; }
    }

    public bool BundleRequested
    {
        get { AssertOwnerThread(); return _bundleRequested; }
    }

    public long Revision
    {
        get { AssertOwnerThread(); return _revision; }
    }

    internal bool TryRegister(out DiagnosticsBundleRegistration? registration)
    {
        AssertOwnerThread();
        if (_registration is not null)
        {
            registration = null;
            return false;
        }
        registration = new DiagnosticsBundleRegistration(this);
        _registration = registration;
        SetStatus(DiagnosticsBundleStatus.Ready);
        return true;
    }

    public DiagnosticsBundleRequestResult RequestBundle()
    {
        AssertOwnerThread();
        if (_registration is null) return DiagnosticsBundleRequestResult.Unavailable;
        if (_bundleRequested || _requestClaimed) return DiagnosticsBundleRequestResult.RequestPending;
        _bundleRequested = true;
        AdvanceRevision();
        return DiagnosticsBundleRequestResult.Accepted;
    }

    internal bool TryTakeRequest(DiagnosticsBundleRegistration registration)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        if (!_bundleRequested || _requestClaimed) return false;
        _requestClaimed = true;
        return true;
    }

    internal bool Publish(DiagnosticsBundleRegistration registration, DiagnosticsBundleStatus status)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        if (status.State == DiagnosticsBundleState.Unavailable)
            throw new ArgumentException("Only the registry may publish unavailable state.", nameof(status));
        _bundleRequested = false;
        _requestClaimed = false;
        return SetStatus(status);
    }

    internal void Remove(DiagnosticsBundleRegistration registration)
    {
        AssertOwnerThread();
        if (!ReferenceEquals(_registration, registration)) return;
        _registration = null;
        _bundleRequested = false;
        _requestClaimed = false;
        SetStatus(DiagnosticsBundleStatus.Unavailable);
    }

    private bool SetStatus(DiagnosticsBundleStatus status)
    {
        if (_status == status) return false;
        _status = status;
        AdvanceRevision();
        return true;
    }

    private void AdvanceRevision() => _revision = checked(_revision + 1);

    private void AssertRegistration(DiagnosticsBundleRegistration registration)
    {
        if (!ReferenceEquals(_registration, registration))
            throw new ObjectDisposedException(nameof(DiagnosticsBundleRegistration));
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Diagnostics-bundle control must remain on its owning main thread.");
    }
}

internal sealed class DiagnosticsBundleRegistration : IDisposable
{
    private DiagnosticsBundleRegistry? _registry;

    internal DiagnosticsBundleRegistration(DiagnosticsBundleRegistry registry) => _registry = registry;

    internal bool TryTakeRequest() => Registry().TryTakeRequest(this);
    internal bool Publish(DiagnosticsBundleStatus status) => Registry().Publish(this, status);

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        _registry = null;
        registry.Remove(this);
    }

    private DiagnosticsBundleRegistry Registry() =>
        _registry ?? throw new ObjectDisposedException(nameof(DiagnosticsBundleRegistration));
}
