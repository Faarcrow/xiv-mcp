using XivMcp.Quests;

namespace XivMcp.Tests;

public class ManualStepTests
{
    [Theory]
    [InlineData("Meld any materia on Iron Lance", "Iron Lance")]
    [InlineData("Meld any materia on the Staghorn Staff.", "Staghorn Staff")]
    [InlineData("Meld materia on a Cotton Acton", "Cotton Acton")]
    [InlineData("meld a Materia I on Goatskin Leggings", "Goatskin Leggings")]
    public void A_meld_step_names_its_gear(string comment, string item) =>
        Assert.Equal(item, ManualStep.MeldItem("WaitForManualProgress", comment));

    [Theory]
    [InlineData("WaitForManualProgress", "Craft the Decorated Bronze Barbut by hand")]
    [InlineData("WaitForManualProgress", null)]
    [InlineData("Interact", "Meld any materia on Iron Lance")]
    public void Other_steps_are_left_to_the_player(string type, string? comment) =>
        Assert.Null(ManualStep.MeldItem(type, comment));
}
