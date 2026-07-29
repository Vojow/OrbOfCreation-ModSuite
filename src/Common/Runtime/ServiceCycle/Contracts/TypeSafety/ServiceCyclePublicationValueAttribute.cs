using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>
/// Marks a Common-owned container as an audited immutable publication value: a type whose own
/// members the structural validators do not re-walk, because Common guarantees by construction that
/// it copies its contents, exposes no array, collection, or mutable view, and holds no runtime
/// ownership. Type arguments are still walked under the full rules of whatever role the value
/// appears in, so marking a container admits the container and never its contents.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a new publication primitive costs one attribute rather than an edit to every
/// validator that would otherwise reject it by shape. Without it, the recurring friction of teaching
/// two validators about each new container is a standing incentive to work around the rules instead
/// of extending them.
/// </para>
/// <para>
/// Wearing this attribute is enough to be honored (see
/// <see cref="ServiceCycleBoundaryRules.IsAuditedPublicationValue"/>); who may wear it is settled by
/// review and pinned mechanically by the exact-set allowlist in
/// <c>ServiceCycleAuditedTypeAllowlistTests</c>, which fails naming any new bearer. That test — not
/// an assembly boundary — is the gate, because the suite is converging on a single DLL in which
/// "declared in Common" would be true of everything.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
internal sealed class ServiceCyclePublicationValueAttribute : Attribute
{
}
