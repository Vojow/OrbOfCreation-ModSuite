using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal static class MentorConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.CanStartMentorActively;

    internal static double ShareFraction(
        SuiteRuntimeConfiguration configuration,
        MasteryExperienceDomain domain) =>
        Math.Clamp(domain switch
        {
            MasteryExperienceDomain.Spell => configuration.Mentor.SpellSharePercent,
            MasteryExperienceDomain.Artifact => configuration.Mentor.ArtifactSharePercent,
            _ => configuration.Mentor.AlchemySharePercent,
        }, 0.0, 100.0) / 100.0;

    internal static bool DomainEnabled(
        SuiteRuntimeConfiguration configuration,
        MasteryExperienceDomain domain) =>
        domain switch
        {
            MasteryExperienceDomain.Spell => true,
            MasteryExperienceDomain.Artifact => configuration.Mentor.ArtifactsEnabled,
            _ => configuration.Mentor.AlchemyEnabled,
        };

    internal static MonotonicDuration IdleInterval =>
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
}
