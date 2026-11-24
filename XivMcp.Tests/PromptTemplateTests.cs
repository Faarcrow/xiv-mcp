using XivMcp.Ui;

namespace XivMcp.Tests;

public class PromptTemplateTests
{
    // Addon row 4385 (English) as text pieces around its item name placeholders.
    private static readonly string[] ReplaceMissing = ["", " registered to this gear set could not be found in your Armoury Chest. Replace it with ", "?"];

    [Fact]
    public void A_prompt_matches_when_its_fixed_pieces_appear_in_order()
    {
        const string prompt = "The weathered saw registered to this gear set could not be found in your Armoury Chest. Replace it with a cosmic saw prototype v0.5?";
        Assert.True(PromptTemplate.Matches(prompt, ReplaceMissing));
    }

    [Fact]
    public void Line_breaks_and_case_in_the_window_do_not_matter()
    {
        const string prompt = "The weathered saw registered to this gear set could not be\nfound in your Armoury Chest. replace it with a cosmic saw?";
        Assert.True(PromptTemplate.Matches(prompt, ReplaceMissing));
    }

    [Fact]
    public void Text_macros_in_the_window_do_not_matter()
    {
        // As read from the yes/no window: a line break macro where the window wraps the text.
        const string prompt = "This item is unique and untradable. Are you \u0002\u0010\u0001\u0003certain you wish to sell it?";
        Assert.True(PromptTemplate.Matches(prompt, ["This item is unique and untradable. Are you certain you wish to sell it?"]));
    }

    [Fact]
    public void Other_prompts_do_not_match()
    {
        Assert.False(PromptTemplate.Matches("Discard the weathered saw?", ReplaceMissing));
        Assert.False(PromptTemplate.Matches("Replace it with a cosmic saw? It was registered to this gear set could not be found in your Armoury Chest.", ReplaceMissing));
    }

    [Fact]
    public void A_template_without_text_matches_nothing()
    {
        Assert.False(PromptTemplate.Matches("Anything?", ["", " "]));
        Assert.False(PromptTemplate.Matches("", ReplaceMissing));
    }

    // Addon rows 2436 and 2437 (English), the warnings buy_item answers by ticking Proceed, as their fixed pieces around the line break.
    private static readonly string[][] EquipWarnings =
    [
        ["You cannot currently equip this item.", "Proceed with the transaction?"],
        ["You will never be able to equip this item.", "Proceed with the transaction?"],
    ];

    private static bool IsEquipWarning(string prompt) => EquipWarnings.Any(w => PromptTemplate.Matches(prompt, w));

    [Fact]
    public void The_equip_warnings_match_as_the_shop_shows_them()
    {
        // As a gil shop showed it in game, with the window's line break.
        Assert.True(IsEquipWarning("You cannot currently equip this item.\nProceed with the transaction?"));
        Assert.True(IsEquipWarning("You will never be able to equip this item.\nProceed with the transaction?"));
    }

    [Fact]
    public void Other_proceed_prompts_are_not_equip_warnings()
    {
        // Same "Proceed" box, different decision: these must be answered No, not ticked.
        Assert.False(IsEquipWarning("Your fisher level is not high enough to use this bait. Proceed with the transaction?"));
        Assert.False(IsEquipWarning("You already own this weapon. Proceed with the transaction?"));
        Assert.False(IsEquipWarning("You are unable to carry any more Fine Sand and will not receive full compensation. Proceed with the transaction?"));
    }
}
