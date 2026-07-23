using System;
using System.Collections.Generic;

namespace OrbModding.Common.Runtime;

public interface IRuntimeDiagnosticsSource
{
    event Action<RuntimeDiagnosticsTransition>? Transitioned;
    IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> GetSnapshot();
}
