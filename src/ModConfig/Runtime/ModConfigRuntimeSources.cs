using System;
using OrbChronicle;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.Verification;
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
        IServiceActionOutcomeWindowSource actionOutcomes,
        IServiceCyclePumpTimingSource pumpTiming,
        IManualFullTraceControl manualFullTrace,
        IHostTraceDumpControl hostTraceDump,
        IDifferentialVerificationControl differentialVerification,
        IDecisionJournalStatusSource decisionJournal,
        IChronicleRuntime chronicle
#if SERVICE_CYCLE_PROFILE
        , IPerformanceProfileControl performanceProfile
#endif
        )
    {
        SchemaStatuses = schemaStatuses ?? throw new ArgumentNullException(nameof(schemaStatuses));
        FeatureStatuses = featureStatuses ?? throw new ArgumentNullException(nameof(featureStatuses));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ActionOutcomes = actionOutcomes ?? throw new ArgumentNullException(nameof(actionOutcomes));
        PumpTiming = pumpTiming ?? throw new ArgumentNullException(nameof(pumpTiming));
        ManualFullTrace = manualFullTrace ?? throw new ArgumentNullException(nameof(manualFullTrace));
        HostTraceDump = hostTraceDump ?? throw new ArgumentNullException(nameof(hostTraceDump));
        DifferentialVerification = differentialVerification ??
                                   throw new ArgumentNullException(nameof(differentialVerification));
        DecisionJournal = decisionJournal ?? throw new ArgumentNullException(nameof(decisionJournal));
        Chronicle = chronicle ?? throw new ArgumentNullException(nameof(chronicle));
#if SERVICE_CYCLE_PROFILE
        PerformanceProfile = performanceProfile ?? throw new ArgumentNullException(nameof(performanceProfile));
#endif
    }

    public IConfigurationSchemaStatusSource SchemaStatuses { get; }
    public IFeatureStatusSource FeatureStatuses { get; }
    public IRuntimeDiagnosticsSource Diagnostics { get; }
    public IServiceActionOutcomeWindowSource ActionOutcomes { get; }
    public IServiceCyclePumpTimingSource PumpTiming { get; }
    public IManualFullTraceControl ManualFullTrace { get; }
    public IHostTraceDumpControl HostTraceDump { get; }
    public IDifferentialVerificationControl DifferentialVerification { get; }
    public IDecisionJournalStatusSource DecisionJournal { get; }
    public IChronicleRuntime Chronicle { get; }
#if SERVICE_CYCLE_PROFILE
    public IPerformanceProfileControl PerformanceProfile { get; }
#endif
}
