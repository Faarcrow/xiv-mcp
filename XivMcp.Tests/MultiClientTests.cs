using System.Collections.Generic;
using System.Linq;
using XivMcp.Connect;
using Xunit;

namespace XivMcp.Tests;

public class PortPlanTests
{
    private static bool AllFree(int _) => true;

    [Fact]
    public void A_known_account_keeps_its_port()
    {
        var ports = new Dictionary<string, int> { ["a"] = 37521, ["b"] = 37522 };
        Assert.Equal(37522, PortPlan.ForAccount("b", ports, 37521, AllFree));
    }

    [Fact]
    public void The_first_account_gets_the_configured_port()
    {
        Assert.Equal(37521, PortPlan.ForAccount("a", new Dictionary<string, int>(), 37521, AllFree));
    }

    [Fact]
    public void A_new_account_gets_the_lowest_port_no_other_account_has()
    {
        var ports = new Dictionary<string, int> { ["a"] = 37521, ["c"] = 37523 };
        Assert.Equal(37522, PortPlan.ForAccount("b", ports, 37521, AllFree));
    }

    [Fact]
    public void A_new_account_skips_ports_another_program_holds()
    {
        var ports = new Dictionary<string, int> { ["a"] = 37521 };
        Assert.Equal(37523, PortPlan.ForAccount("b", ports, 37521, p => p != 37522));
    }

    [Fact]
    public void Before_login_the_lowest_free_port_is_taken()
    {
        Assert.Equal(37521, PortPlan.BeforeLogin(37521, AllFree));
        Assert.Equal(37522, PortPlan.BeforeLogin(37521, p => p != 37521));
    }

    [Fact]
    public void Merged_accounts_keep_one_port()
    {
        var ports = new Dictionary<string, int> { ["old"] = 37522 };
        PortPlan.Rename(ports, "old", "new");
        Assert.Equal(new Dictionary<string, int> { ["new"] = 37522 }, ports);

        var both = new Dictionary<string, int> { ["old"] = 37522, ["new"] = 37521 };
        PortPlan.Rename(both, "old", "new");
        Assert.Equal(new Dictionary<string, int> { ["new"] = 37521 }, both);
    }
}

public class SharedListTests
{
    private sealed record Job(string Id, string? Owner, string State);

    [Fact]
    public void Each_client_writes_its_own_jobs_and_keeps_the_others_from_disk()
    {
        var disk = new List<Job> { new("1", "a", "running-on-disk"), new("2", "b", "paused-on-disk") };
        // Client "b" still holds an old copy of job 1 and its own job 2 updated, plus a new job 3.
        var memory = new List<Job> { new("1", "a", "stale"), new("2", "b", "done"), new("3", "b", "queued") };
        var merged = SharedList.Merge(disk, memory, j => j.Id, j => j.Owner == "b");
        Assert.Equal(["1:running-on-disk", "2:done", "3:queued"], merged.Select(j => $"{j.Id}:{j.State}").Order());
    }

    [Fact]
    public void A_job_the_client_dropped_stays_dropped()
    {
        var disk = new List<Job> { new("1", "b", "finished-long-ago"), new("2", "a", "paused") };
        var merged = SharedList.Merge(disk, new List<Job>(), j => j.Id, j => j.Owner == "b");
        Assert.Equal(["2"], merged.Select(j => j.Id));
    }
}
