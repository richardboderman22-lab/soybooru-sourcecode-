namespace Nuuru.Tools.AuthFlowChart.Models;

public enum PermissionSource
{
    Attribute,  // From [Authorize(Policy = "...")] attribute
    Inline      // From User.HasPermission() call in method body
}

public class PermissionCheck
{
    public string Permission { get; set; } = string.Empty;
    public PermissionSource Source { get; set; }

    public PermissionCheck() { }

    public PermissionCheck(string permission, PermissionSource source)
    {
        Permission = permission;
        Source = source;
    }

    public string Category => Permission.Split('.').FirstOrDefault() ?? "unknown";
}
