using EVBlocker.Core.Firewall;

namespace EVBlocker.Core.Tests;

public sealed class BootGuardFiltersTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Block_CoversTheWholeBootGap_OnBothAddressFamilies(bool ipv6)
    {
        // Boot-time alone ends when the filtering engine starts; persistent alone starts then.
        // Missing either leaves part of the gap open.
        GuardFilterLifetime[] lifetimes = BootGuardFilters.Guard
            .Where(f => f.Role == GuardFilterRole.Block && f.IPv6 == ipv6)
            .Select(f => f.Lifetime)
            .Order()
            .ToArray();

        Assert.Equal([GuardFilterLifetime.BootTime, GuardFilterLifetime.Persistent], lifetimes);
    }

    [Fact]
    public void EveryPassFilter_HasAMatchingBlockLifetime()
    {
        // A pass filter that exists only in one phase would leave loopback or DHCP blocked in the
        // other, and a machine without DHCP during boot comes up without an address.
        foreach (GuardFilterRole role in new[] { GuardFilterRole.Loopback, GuardFilterRole.Dhcp })
        {
            foreach (bool ipv6 in new[] { false, true })
            {
                GuardFilterLifetime[] lifetimes = BootGuardFilters.Guard
                    .Where(f => f.Role == role && f.IPv6 == ipv6)
                    .Select(f => f.Lifetime)
                    .Order()
                    .ToArray();

                Assert.Equal([GuardFilterLifetime.BootTime, GuardFilterLifetime.Persistent], lifetimes);
            }
        }
    }

    [Fact]
    public void Release_DoesNotSurviveAReboot()
    {
        // The whole design rests on this: a reboot must discard the release so the block takes
        // over. A persistent release would make the guard do nothing, silently.
        Assert.All(BootGuardFilters.Release, f =>
        {
            Assert.Equal(GuardFilterRole.Release, f.Role);
            Assert.Equal(GuardFilterLifetime.Runtime, f.Lifetime);
        });

        Assert.Equal([false, true], BootGuardFilters.Release.Select(f => f.IPv6).Order());
    }

    [Fact]
    public void Guard_HasNoRuntimeFilters()
    {
        // A runtime filter in the guard would be gone after the reboot it exists for.
        Assert.DoesNotContain(BootGuardFilters.Guard, f => f.Lifetime == GuardFilterLifetime.Runtime);
        Assert.DoesNotContain(BootGuardFilters.Guard, f => f.Role == GuardFilterRole.Release);
    }

    [Fact]
    public void Weights_ReleaseOutweighsPassOutweighsBlock()
    {
        // Within a sublayer the heaviest matching filter decides. The release must beat the
        // block, and loopback and DHCP must beat it too while the guard is engaged.
        Assert.All(BootGuardFilters.Release, f => Assert.Equal(BootGuardFilters.ReleaseWeight, f.Weight));
        Assert.All(BootGuardFilters.Guard.Where(f => f.Role == GuardFilterRole.Block), f => Assert.Equal(BootGuardFilters.BlockWeight, f.Weight));
        Assert.All(BootGuardFilters.Guard.Where(f => f.Role is GuardFilterRole.Loopback or GuardFilterRole.Dhcp), f => Assert.Equal(BootGuardFilters.PassWeight, f.Weight));

        Assert.True(BootGuardFilters.ReleaseWeight > BootGuardFilters.PassWeight);
        Assert.True(BootGuardFilters.PassWeight > BootGuardFilters.BlockWeight);

        // FWP_UINT8 weights are only honoured from 0 to 15.
        Assert.True(BootGuardFilters.ReleaseWeight <= 15);
    }

    [Fact]
    public void Keys_AreUniqueAndDistinctFromProviderAndSubLayer()
    {
        Guid[] keys = BootGuardFilters.All.Select(f => f.Key)
            .Append(BootGuardFilters.ProviderKey)
            .Append(BootGuardFilters.SubLayerKey)
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, keys);
    }

    [Fact]
    public void Names_AreUniqueAndSayWhoOwnsThem()
    {
        // Seen by whoever runs 'netsh wfp show filters' trying to work out why nothing connects.
        string[] names = BootGuardFilters.All.Select(f => f.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, n => Assert.StartsWith("EVBlocker", n, StringComparison.Ordinal));
    }

    [Fact]
    public void DhcpServerPort_IsTheServerSideOfEachProtocol()
    {
        Assert.Equal(67, BootGuardFilters.DhcpServerPort(ipv6: false));
        Assert.Equal(547, BootGuardFilters.DhcpServerPort(ipv6: true));
    }
}
