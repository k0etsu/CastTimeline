using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CastTimeline.Extensions;

public static class ImGuiExt
{
    // Renders a status message with a separator and colour-coded label.
    // Green for success messages, red for messages that start with "Error".
    public static void DrawStatusMessage(string status)
    {
        ImGui.Separator();
        ImGui.Text("Status:");
        var color = status.StartsWith("Error") ? new Vector4(1, 0, 0, 1) : new Vector4(0, 1, 0, 1);
        ImGui.TextColored(color, status);
    }
}
