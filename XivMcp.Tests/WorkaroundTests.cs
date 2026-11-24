using XivMcp.Integrations;

namespace XivMcp.Tests;

public class WorkaroundTests
{
    [Fact]
    public void Every_workaround_says_what_it_does_what_the_plugin_does_and_the_version_it_was_checked_against()
    {
        var all = IntegrationCatalog.All.SelectMany(i => i.Workarounds).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, w =>
        {
            Assert.Matches("^[a-z_]+$", w.Id);
            Assert.False(string.IsNullOrWhiteSpace(w.Does), w.Id);
            Assert.False(string.IsNullOrWhiteSpace(w.Without), w.Id);
            Assert.Matches(@"^\d+(\.\d+){1,3}$", w.CheckedAgainst);
        });
        Assert.Equal(all.Count, all.Select(w => w.Id).Distinct().Count());
    }

    [Fact]
    public void Workaround_texts_use_no_contractions() =>
        Assert.All(IntegrationCatalog.All.SelectMany(i => i.Workarounds), w =>
        {
            Assert.DoesNotMatch(@"(n't|'re|'ve|'ll|'d|(it|that|there|what)'s|I'm)", w.Does);
            Assert.DoesNotMatch(@"(n't|'re|'ve|'ll|'d|(it|that|there|what)'s|I'm)", w.Without);
        });

    [Fact]
    public void Questionable_declares_its_workarounds()
    {
        var questionable = IntegrationCatalog.All.Single(i => i.PluginId == "Questionable");
        Assert.Equal(QuestionableWorkarounds.All, questionable.Workarounds);
    }

    [Fact]
    public void Satisfier_declares_its_workarounds()
    {
        var satisfier = IntegrationCatalog.All.Single(i => i.PluginId == "vsatisfy");
        Assert.Equal(SatisfierWorkarounds.All, satisfier.Workarounds);
    }

    [Fact]
    public void Integrations_without_workarounds_declare_none()
    {
        Assert.Empty(IntegrationCatalog.All.Single(i => i.PluginId == "AutoDuty").Workarounds);
    }

    [Fact]
    public void A_log_counts_each_workaround_in_the_order_first_used()
    {
        var log = new WorkaroundLog("Questionable", "15.756.3.70");
        log.Used(QuestionableWorkarounds.ConfirmHqTurnIn);
        log.Used(QuestionableWorkarounds.PickJob);
        log.Used(QuestionableWorkarounds.ConfirmHqTurnIn);

        var report = log.Report();
        Assert.Equal("Questionable", report.Plugin);
        Assert.Equal("15.756.3.70", report.Version);
        Assert.Equal(["confirm_hq_turn_in", "pick_job"], report.Used.Select(u => u.Id));
        Assert.Equal([2, 1], report.Used.Select(u => u.Count));
        Assert.Equal(QuestionableWorkarounds.ConfirmHqTurnIn.Does, report.Used[0].Does);
        Assert.Equal(QuestionableWorkarounds.ConfirmHqTurnIn.CheckedAgainst, report.Used[0].CheckedAgainst);
    }

    [Fact]
    public void A_run_that_used_none_still_names_the_version()
    {
        var report = new WorkaroundLog("Questionable", "16.0.0.1").Report();
        Assert.Empty(report.Used);
        Assert.Equal("16.0.0.1", report.Version);
    }
}
