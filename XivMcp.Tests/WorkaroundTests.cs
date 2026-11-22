using XivMcp.Integrations;

namespace XivMcp.Tests;

public class WorkaroundTests
{
    [Fact]
    public void Every_workaround_says_what_it_does_and_what_happens_without_it()
    {
        var all = IntegrationCatalog.All.SelectMany(i => i.Workarounds).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, w =>
        {
            Assert.Matches("^[a-z_]+$", w.Id);
            Assert.False(string.IsNullOrWhiteSpace(w.Does), w.Id);
            Assert.False(string.IsNullOrWhiteSpace(w.Instead), w.Id);
        });
        Assert.Equal(all.Count, all.Select(w => w.Id).Distinct().Count());
    }

    [Fact]
    public void Questionable_declares_its_workarounds()
    {
        var questionable = IntegrationCatalog.ForPlugin("questionable")!;
        Assert.Equal("Questionable", questionable.PluginId);
        Assert.Equal(QuestionableWorkarounds.All, questionable.Workarounds);
    }

    [Fact]
    public void Integrations_without_workarounds_declare_none()
    {
        Assert.Empty(IntegrationCatalog.ForPlugin("AutoDuty")!.Workarounds);
        Assert.Null(IntegrationCatalog.ForPlugin("NoSuchPlugin"));
    }

    [Fact]
    public void A_log_counts_each_workaround_in_the_order_first_used()
    {
        var log = new WorkaroundLog(() => true);
        log.Used(QuestionableWorkarounds.ConfirmHqTurnIn);
        log.Used(QuestionableWorkarounds.PickJob);
        log.Used(QuestionableWorkarounds.ConfirmHqTurnIn);

        var report = log.Report()!;
        Assert.Equal(["confirm_hq_turn_in", "pick_job"], report.Select(u => u.Id));
        Assert.Equal([2, 1], report.Select(u => u.Count));
        Assert.Equal(QuestionableWorkarounds.ConfirmHqTurnIn.Does, report[0].Does);
    }

    [Fact]
    public void A_log_with_nothing_used_reports_nothing()
    {
        Assert.Null(new WorkaroundLog(() => true).Report());
    }

    [Fact]
    public void The_switch_is_read_on_every_check()
    {
        var on = true;
        var log = new WorkaroundLog(() => on);
        Assert.True(log.Enabled);
        on = false;
        Assert.False(log.Enabled);
    }
}
