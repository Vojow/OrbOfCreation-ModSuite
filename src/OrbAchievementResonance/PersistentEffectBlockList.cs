using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace OrbAchievementResonance;

internal sealed class PersistentEffectBlockList
{
    private readonly object _owner;
    private readonly MemberInfo _member;

    private PersistentEffectBlockList(object owner, MemberInfo member, object? collection)
    {
        _owner = owner;
        _member = member;
        Collection = collection;
    }

    public object? Collection { get; private set; }

    public static bool TryFind(object owner, out PersistentEffectBlockList list)
    {
        foreach (var member in NativeReflection.GetReadableMembers(owner.GetType()))
        {
            if (!string.Equals(member.Name, "persistentEffectBlocks", StringComparison.Ordinal) &&
                !string.Equals(member.Name, "PersistentEffectBlocks", StringComparison.Ordinal))
            {
                continue;
            }

            list = new PersistentEffectBlockList(owner, member, NativeReflection.GetValue(owner, member));
            return true;
        }

        list = null!;
        return false;
    }

    public int RemoveOwnedBlocks()
    {
        if (Collection is null)
        {
            return 0;
        }

        if (Collection is IList list && !Collection.GetType().IsArray)
        {
            var removed = 0;
            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (NativeReflection.ContainsOwnedUuid(list[index]))
                {
                    list.RemoveAt(index);
                    removed++;
                }
            }

            return removed;
        }

        var collectionType = Collection.GetType();
        if (!collectionType.IsArray)
        {
            return 0;
        }

        var source = ((IEnumerable)Collection).Cast<object?>().ToArray();
        var kept = source.Where(item => !NativeReflection.ContainsOwnedUuid(item)).ToArray();
        if (kept.Length == source.Length)
        {
            return 0;
        }

        var array = Array.CreateInstance(collectionType.GetElementType()!, kept.Length);
        for (var index = 0; index < kept.Length; index++)
        {
            array.SetValue(kept[index], index);
        }

        if (NativeReflection.SetMemberValue(_owner, _member, array))
        {
            Collection = array;
            return source.Length - kept.Length;
        }

        return 0;
    }

    public bool ContainsOwnedBlock(string modifierUuid)
    {
        if (Collection is not IEnumerable source)
        {
            return false;
        }

        foreach (var item in source)
        {
            if (NativeReflection.ContainsUuid(item, modifierUuid))
            {
                return true;
            }
        }

        return false;
    }

    public bool Add(object block)
    {
        if (Collection is IList list && !Collection.GetType().IsArray)
        {
            list.Add(block);
            return true;
        }

        var memberType = NativeReflection.GetMemberType(_member);
        if (memberType.IsArray)
        {
            var current = Collection is IEnumerable source ? source.Cast<object?>().ToArray() : Array.Empty<object?>();
            var elementType = memberType.GetElementType() ?? block.GetType();
            var array = Array.CreateInstance(elementType, current.Length + 1);
            for (var index = 0; index < current.Length; index++)
            {
                array.SetValue(current[index], index);
            }

            array.SetValue(block, current.Length);
            if (NativeReflection.SetMemberValue(_owner, _member, array))
            {
                Collection = array;
                return true;
            }
        }

        return false;
    }
}
