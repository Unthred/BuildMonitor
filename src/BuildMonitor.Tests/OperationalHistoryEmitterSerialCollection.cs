namespace BuildMonitor.Tests;

/// <summary>
/// Serializes emitter integration tests that drive <see cref="Infrastructure.LocalBuild.DotNetCliRunner"/>
/// (runner keeps a single active process).
/// </summary>
[CollectionDefinition("OperationalHistoryEmitter.Serial", DisableParallelization = true)]
public sealed class OperationalHistoryEmitterSerialCollection;
