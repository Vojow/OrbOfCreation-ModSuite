using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OrbModding.Common;

namespace OrbMentor;

internal sealed class MentorAlchemyDomainGate : IDisposable
{
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }

    private readonly AlchemyGameplayDomainClassifier _classifier;
    private readonly Dictionary<object, AlchemyDomainClassification> _cache =
        new(ReferenceComparer.Instance);

    public MentorAlchemyDomainGate(AlchemyGameplayDomainClassifier? classifier = null)
    {
        _classifier = classifier ?? new AlchemyGameplayDomainClassifier();
    }

    public AlchemyDomainClassifierStatus Status => _classifier.Status;
    public string StatusReason => _classifier.StatusReason;
    public int LifecycleGeneration => _classifier.LifecycleGeneration;

    public bool TryInitialize(out string reason) => _classifier.TryInitialize(out reason);

    public AlchemyDomainClassification ClassifyAndCache(object? recipe)
    {
        var classification = _classifier.ClassifyRecipe(recipe);
        if (recipe is not null) _cache[recipe] = classification;
        return classification;
    }

    public bool TryGetCached(object recipe, out AlchemyDomainClassification classification) =>
        _cache.TryGetValue(recipe, out classification!);

    public void InvalidateLifecycle()
    {
        _cache.Clear();
        _classifier.InvalidateLifecycle();
    }

    public void Dispose() => _classifier.Dispose();
}
