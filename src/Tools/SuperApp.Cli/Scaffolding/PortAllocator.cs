using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Scaffolding;

/// <summary>Picks free local ports for new components, following the layout of the existing ones.</summary>
/// <remarks>
/// Domain services: Api on <c>51xx</c> and Worker ten higher (Knowledge 5101/5111, SleepDiary 5102/5112), outside the bands of BFFs
/// (<c>5120–5129</c>) and of the analytics forwarder (<c>5180–5189</c>). BFFs: <c>5120–5129</c> (Example 5120). A port is free when no launch
/// profile and no compose service uses it.
/// </remarks>
internal static class PortAllocator
{
    /// <summary>Returns free ports for the Api and Worker of a new service.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <returns>The Api port and the Worker port (Api + 10).</returns>
    /// <exception cref="ScaffoldException">No pair is free in <c>5101–5179</c>.</exception>
    public static (int Api, int Worker) ForService(RepositoryModel model)
    {
        var used = model.Ports.Select(port => port.Port).ToHashSet();
        bool Reserved(int port) => port is >= 5120 and <= 5129 or >= 5180 and <= 5189;
        for (var api = 5101; api + 10 <= 5179; api++)
        {
            if (!Reserved(api) && !Reserved(api + 10) && !used.Contains(api) && !used.Contains(api + 10))
            {
                return (api, api + 10);
            }
        }

        throw new ScaffoldException("No free pair of ports in 5101–5179; give the service ports by hand (launchSettings.json, docker-compose.yml).");
    }

    /// <summary>Returns a free port for a new BFF.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <returns>The first free port of <c>5120–5129</c>.</returns>
    /// <exception cref="ScaffoldException">All ten are taken.</exception>
    public static int ForBff(RepositoryModel model)
    {
        var used = model.Ports.Select(port => port.Port).ToHashSet();
        return Enumerable.Range(5120, 10).FirstOrDefault(port => !used.Contains(port)) is var port and > 0
            ? port
            : throw new ScaffoldException("No free port in 5120–5129 for a BFF; give it a port by hand.");
    }
}
