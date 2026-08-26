using Xunit;

namespace Gym_Management.Tests;

/// <summary>Shared API fixture: one LocalDB database per test run, shared by all tests in the collection.</summary>
public class GymApiFixture : IAsyncLifetime
{
    public GymApiFactory Factory { get; } = new();

    public Task InitializeAsync() => Factory.StartAsync();

    public Task DisposeAsync() => Factory.ShutdownAsync();
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<GymApiFixture>;
