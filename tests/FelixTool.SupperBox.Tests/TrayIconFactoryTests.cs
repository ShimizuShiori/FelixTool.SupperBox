namespace FelixTool.SupperBox.Tests;

public sealed class TrayIconFactoryTests
{
    [Fact]
    public void Create_ReturnsTraySizedIcon()
    {
        using var icon = TrayIconFactory.Create();

        Assert.Equal(32, icon.Width);
        Assert.Equal(32, icon.Height);
    }
}
