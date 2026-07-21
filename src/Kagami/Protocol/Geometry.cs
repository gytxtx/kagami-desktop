using System.Text.Json.Serialization;

namespace Kagami.Protocol;

public class WindowInfo
{
    [JsonPropertyName("hwnd")]
    public string Hwnd { get; init; } = "";

    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("process_name")]
    public string ProcessName { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("class_name")]
    public string ClassName { get; init; } = "";

    [JsonPropertyName("visible")]
    public bool Visible { get; init; }

    [JsonPropertyName("cloaked")]
    public bool Cloaked { get; init; }

    [JsonPropertyName("minimized")]
    public bool Minimized { get; init; }

    [JsonPropertyName("foreground")]
    public bool Foreground { get; init; }

    [JsonPropertyName("rect")]
    public Rect Rect { get; init; } = new();
}

public class Rect
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("w")]
    public int W { get; init; }

    [JsonPropertyName("h")]
    public int H { get; init; }
}

public class DetailedRect
{
    [JsonPropertyName("left")]
    public int Left { get; init; }

    [JsonPropertyName("top")]
    public int Top { get; init; }

    [JsonPropertyName("right")]
    public int Right { get; init; }

    [JsonPropertyName("bottom")]
    public int Bottom { get; init; }
}

public class Point
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }
}
