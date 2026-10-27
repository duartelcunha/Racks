using System.Runtime.CompilerServices;
using Racks.Infrastructure.Logging;

namespace Racks.Tests.Fakes;

/// <summary>
/// Tests that reach FileOperationBroker.Default (WorkspaceReturn, Magic Organize undo...) must not append to
/// the developer's real %AppData%\Racks\audit.log. Runs once when the test assembly loads.
/// </summary>
internal static class TestAuditSetup
{
    [ModuleInitializer]
    internal static void Init() => AuditLog.Current = NullAuditSink.Instance;
}
