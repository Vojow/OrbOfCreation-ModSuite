using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One view as published: whether it is active, and whether it is always active.</summary>
internal readonly struct WorldView : IWorldEntity
{
    internal WorldView(
        Guid viewId,
        bool active,
        bool alwaysActive)
    {
        ViewId = viewId;
        Active = active;
        AlwaysActive = alwaysActive;
    }

    internal Guid ViewId { get; }

    public Guid EntityId => ViewId;

    internal bool Active { get; }

    internal bool AlwaysActive { get; }
}

internal sealed class WorldViewBinder : WorldPlainBinder<WorldView>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _active;
    private Func<object, bool>? _alwaysActive;

    internal override string Category => "views";

    internal override string TypeName => "ViewSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _active = bind.Field<bool>("active");
        _alwaysActive = bind.Field<bool>("alwaysActive");
        return bind.Failure;
    }

    internal override WorldView Read(object entity) =>
        new(
            _id!(entity),
            _active!(entity),
            _alwaysActive!(entity));
}
