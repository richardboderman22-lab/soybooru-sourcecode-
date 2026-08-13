namespace Nuuru.Tools.AuthFlowChart.Models;

public class EndpointInfo
{
    public string HttpMethod { get; set; } = string.Empty;      // GET, POST, PUT, DELETE, PATCH
    public string RouteTemplate { get; set; } = string.Empty;   // Relative route (e.g., "{id}/file")
    public string MethodName { get; set; } = string.Empty;      // C# method name
    public AuthLevel AuthLevel { get; set; }
    public string? AttributePermission { get; set; }            // Permission from [Authorize(Policy = ...)]
    public List<PermissionCheck> InlineChecks { get; set; } = new();
    public bool InheritsFromController { get; set; }            // True if auth is inherited from controller

    public IEnumerable<PermissionCheck> AllPermissions
    {
        get
        {
            if (!string.IsNullOrEmpty(AttributePermission))
            {
                yield return new PermissionCheck(AttributePermission, PermissionSource.Attribute);
            }
            foreach (var check in InlineChecks)
            {
                yield return check;
            }
        }
    }

    public string FullRoute(string controllerRoutePrefix)
    {
        var prefix = controllerRoutePrefix.TrimEnd('/');
        var route = RouteTemplate.TrimStart('/');

        if (string.IsNullOrEmpty(route))
            return prefix;

        return $"{prefix}/{route}";
    }
}
