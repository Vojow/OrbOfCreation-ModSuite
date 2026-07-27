using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif

namespace OrbModConfig;

internal sealed class ModConfigRuntimeSources
{
    public ModConfigRuntimeSources(
        IConfigurationSchemaStatusSource schemaStatuses,
        IFeatureStatusSource featureStatuses,
        IRuntimeDiagnosticsSource diagnostics,
        IServiceCyclePumpTimingSource pumpTiming,
        IManualFullTraceControl manualFullTrace,
        IHostTraceDumpControl hostTraceDump,
        IDecisionJournalStatusSource decisionJournal
#if SERVICE_CYCLE_PROFILE
        , IPerformanceProfileControl performanceProfile
#endif
        )
    {
        SchemaStatuses = schemaStatuses ?? throw new ArgumentNullException(nameof(schemaStatuses));
        FeatureStatuses = featureStatuses ?? throw new ArgumentNullException(nameof(featureStatuses));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        PumpTiming = pumpTiming ?? throw new ArgumentNullException(nameof(pumpTiming));
        ManualFullTrace = manualFullTrace ?? throw new ArgumentNullException(nameof(manualFullTrace));
        HostTraceDump = hostTraceDump ?? throw new ArgumentNullException(nameof(hostTraceDump));
        DecisionJournal = decisionJournal ?? throw new ArgumentNullException(nameof(decisionJournal));
#if SERVICE_CYCLE_PROFILE
        PerformanceProfile = performanceProfile ?? throw new ArgumentNullException(nameof(performanceProfile));
#endif
    }

    public IConfigurationSchemaStatusSource SchemaStatuses { get; }
    public IFeatureStatusSource FeatureStatuses { get; }
    public IRuntimeDiagnosticsSource Diagnostics { get; }
    public IServiceCyclePumpTimingSource PumpTiming { get; }
    public IManualFullTraceControl ManualFullTrace { get; }
    public IHostTraceDumpControl HostTraceDump { get; }
    public IDecisionJournalStatusSource DecisionJournal { get; }
#if SERVICE_CYCLE_PROFILE
    public IPerformanceProfileControl PerformanceProfile { get; }
#endif
}
