using VsrSwitcher.Tray;

namespace VsrSwitcher.Tests;

public class HotkeySettingTests
{
    [Fact]
    public void The_default_is_ctrl_alt_shift_v()
    {
        Assert.Equal("Ctrl+Alt+Shift+V", HotkeySetting.Format(HotkeySetting.Default));
        Assert.Null(HotkeySetting.Problem(HotkeySetting.Default));
        Assert.Null(HotkeySetting.Caution(HotkeySetting.Default));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+V")]
    [InlineData("Ctrl+Shift+V")]
    [InlineData("Alt+F9")]
    [InlineData("Ctrl+Alt+1")]
    [InlineData("Ctrl+F13")]
    public void Saved_text_round_trips(string text)
    {
        Assert.True(HotkeySetting.TryParse(text, out var hotkey));
        Assert.Equal(text, HotkeySetting.Format(hotkey));
    }

    [Fact]
    public void Parsing_ignores_case_and_spaces()
    {
        Assert.True(HotkeySetting.TryParse(" ctrl + alt + v ", out var hotkey));
        Assert.Equal(Keys.Control | Keys.Alt | Keys.V, hotkey);
    }

    [Fact]
    public void Digits_are_saved_as_digits()
    {
        Assert.True(HotkeySetting.TryParse("Ctrl+Alt+1", out var hotkey));
        Assert.Equal(Keys.Control | Keys.Alt | Keys.D1, hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("V+Ctrl")]
    [InlineData("Ctrl+Control")]
    [InlineData("Win+V")]
    [InlineData("Ctrl+Banana")]
    public void Malformed_text_is_rejected(string text)
    {
        Assert.False(HotkeySetting.TryParse(text, out _));
    }

    [Theory]
    [InlineData(Keys.V)]                                         // no modifier at all
    [InlineData(Keys.Shift | Keys.V)]                            // Shift alone would block typing capitals
    [InlineData(Keys.F5)]                                        // a bare function key is still refused
    [InlineData(Keys.Control | Keys.Alt | Keys.F12)]             // reserved by Windows
    [InlineData(Keys.Control | Keys.Alt | Keys.ShiftKey)]        // modifiers but no key
    public void Unusable_combinations_have_a_problem(Keys hotkey)
    {
        Assert.NotNull(HotkeySetting.Problem(hotkey));
    }

    [Theory]
    [InlineData(Keys.Alt | Keys.F9)]
    [InlineData(Keys.Control | Keys.Shift | Keys.V)]
    [InlineData(Keys.Control | Keys.Alt | Keys.V)]
    public void Usable_combinations_have_no_problem(Keys hotkey)
    {
        Assert.Null(HotkeySetting.Problem(hotkey));
    }

    [Fact]
    public void Ctrl_alt_without_shift_comes_with_the_altgr_caution()
    {
        Assert.NotNull(HotkeySetting.Caution(Keys.Control | Keys.Alt | Keys.V));
        Assert.Null(HotkeySetting.Caution(Keys.Alt | Keys.V));
    }
}
