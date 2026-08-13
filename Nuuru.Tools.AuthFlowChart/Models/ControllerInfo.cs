namespace Nuuru.Tools.AuthFlowChart.Models;

public class ControllerInfo
{
    public string Name { get; set; } = string.Empty;            // e.g., "PostController"
    public string DisplayName => Name.EndsWith("Controller")
        ? Name[..^"Controller".Length]
        : Name;
    public string RoutePrefix { get; set; } = string.Empty;     // e.g., "api/booru/posts"
    public AuthLevel DefaultAuthLevel { get; set; }
    public string? DefaultPermission { get; set; }
    public List<EndpointInfo> Endpoints { get; set; } = new();
}
