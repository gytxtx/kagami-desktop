using System.Text.Json;
using Kagami.Protocol;

namespace Kagami.Tests.Protocol;

public class LocatorTests
{
    [Fact]
    public void Deserialize_LocatorWithoutView_DefaultsToControl()
    {
        const string json = """
            {"window":{"hwnd":"0x1234"},"path":[]}
            """;

        var locator = JsonSerializer.Deserialize<Locator>(json, JsonConfig.Options);

        Assert.NotNull(locator);
        Assert.Equal("control", locator!.View);
    }

    [Fact]
    public void Serialize_EmptyLocator_ProducesCorrectJson()
    {
        var locator = new Locator
        {
            Window = new WindowRef { Hwnd = "0x1234" },
            Path = new List<LocatorSegment>()
        };

        var json = JsonSerializer.Serialize(locator, JsonConfig.Options);

        var deserialized = JsonSerializer.Deserialize<Locator>(json, JsonConfig.Options);
        Assert.NotNull(deserialized);
        Assert.Equal("0x1234", deserialized!.Window.Hwnd);
        Assert.Empty(deserialized.Path);
    }

    [Fact]
    public void Serialize_MultiSegmentPath_RoundTrips()
    {
        var locator = new Locator
        {
            Window = new WindowRef { Hwnd = "0xAABB" },
            Path = new List<LocatorSegment>
            {
                new()
                {
                    ControlType = "Window",
                    AutomationId = "MainWindow",
                    Name = "Cafe Launcher",
                    ClassName = "Window",
                    Ordinal = 0
                },
                new()
                {
                    ControlType = "Button",
                    AutomationId = "BtnLogin",
                    Name = "Login",
                    ClassName = "Button",
                    Ordinal = 2
                }
            }
        };

        var json = JsonSerializer.Serialize(locator, JsonConfig.Options);
        var deserialized = JsonSerializer.Deserialize<Locator>(json, JsonConfig.Options);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Path.Count);
        Assert.Equal("Button", deserialized.Path[1].ControlType);
        Assert.Equal("BtnLogin", deserialized.Path[1].AutomationId);
        Assert.Equal(2, deserialized.Path[1].Ordinal);
    }
}
