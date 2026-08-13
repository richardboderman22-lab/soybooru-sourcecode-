using System.Text;
using Nuuru.Tools.AuthFlowChart.Models;

namespace Nuuru.Tools.AuthFlowChart.Output;

/// <summary>
/// Generates Mermaid flowchart diagrams from controller analysis results
/// </summary>
public class MermaidGenerator
{
    public string Generate(List<ControllerInfo> controllers)
    {
        var sb = new StringBuilder();

        sb.AppendLine("flowchart TB");
        sb.AppendLine();

        // Style definitions
        sb.AppendLine("    %% Style definitions for auth levels");
        sb.AppendLine("    classDef anonymous fill:#90EE90,stroke:#228B22,color:#000");
        sb.AppendLine("    classDef authenticated fill:#87CEEB,stroke:#4169E1,color:#000");
        sb.AppendLine("    classDef userPerm fill:#FFB347,stroke:#FF8C00,color:#000");
        sb.AppendLine("    classDef modPerm fill:#DDA0DD,stroke:#8B008B,color:#000");
        sb.AppendLine("    classDef adminPerm fill:#FF6B6B,stroke:#DC143C,color:#000");
        sb.AppendLine();

        foreach (var controller in controllers)
        {
            GenerateControllerSubgraph(sb, controller);
        }

        return sb.ToString();
    }

    public void GenerateMarkdownFile(List<ControllerInfo> controllers, string outputPath)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Nuuru API Authentication Flowchart");
        sb.AppendLine();
        sb.AppendLine($"Generated on: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("## Legend");
        sb.AppendLine();
        sb.AppendLine("| Color | Auth Level | Description |");
        sb.AppendLine("|-------|------------|-------------|");
        sb.AppendLine("| Green | Anonymous | No authentication required |");
        sb.AppendLine("| Blue | Authenticated | Any logged-in user |");
        sb.AppendLine("| Orange | User | User-level permission (`user.*`) |");
        sb.AppendLine("| Purple | Moderation | Moderation permission (`moderation.*`) |");
        sb.AppendLine("| Red | Admin | Admin permission (`admin.*`) |");
        sb.AppendLine();
        sb.AppendLine("**Note:** Permissions marked with `(runtime)` are checked inline in the code, not via attributes.");
        sb.AppendLine();
        sb.AppendLine("## Diagram");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.Append(Generate(controllers));
        sb.AppendLine("```");
        sb.AppendLine();

        // Add summary table
        GenerateSummaryTable(sb, controllers);

        // Add detailed endpoint list
        GenerateDetailedList(sb, controllers);

        File.WriteAllText(outputPath, sb.ToString());
    }

    private void GenerateControllerSubgraph(StringBuilder sb, ControllerInfo controller)
    {
        var nodePrefix = SanitizeId(controller.DisplayName);

        sb.AppendLine($"    subgraph {nodePrefix}[\"{controller.DisplayName}<br/>{controller.RoutePrefix}\"]");

        int nodeIndex = 0;
        foreach (var endpoint in controller.Endpoints)
        {
            var nodeId = $"{nodePrefix}_{nodeIndex++}";
            var nodeLabel = BuildNodeLabel(endpoint, controller);
            var styleClass = GetStyleClass(endpoint);

            sb.AppendLine($"        {nodeId}[\"{nodeLabel}\"]:::{styleClass}");
        }

        sb.AppendLine("    end");
        sb.AppendLine();
    }

    private string BuildNodeLabel(EndpointInfo endpoint, ControllerInfo controller)
    {
        var parts = new List<string>();

        // HTTP Method and Route
        var route = endpoint.RouteTemplate;
        if (string.IsNullOrEmpty(route))
            route = "/";
        else if (!route.StartsWith("/"))
            route = "/" + route;

        parts.Add($"{endpoint.HttpMethod} {route}");

        // Attribute permission
        if (!string.IsNullOrEmpty(endpoint.AttributePermission))
        {
            parts.Add(endpoint.AttributePermission);
        }

        // Inline permissions
        foreach (var check in endpoint.InlineChecks)
        {
            parts.Add($"{check.Permission} (runtime)");
        }

        return string.Join("<br/>", parts);
    }

    private string GetStyleClass(EndpointInfo endpoint)
    {
        // Determine the highest level of auth required
        var allPermissions = endpoint.AllPermissions.ToList();

        if (allPermissions.Any(p => p.Permission.StartsWith("admin.")))
            return "adminPerm";

        if (allPermissions.Any(p => p.Permission.StartsWith("moderation.")))
            return "modPerm";

        if (allPermissions.Any(p => p.Permission.StartsWith("user.")))
            return "userPerm";

        if (endpoint.AuthLevel == AuthLevel.Authenticated)
            return "authenticated";

        return "anonymous";
    }

    private static string SanitizeId(string name)
    {
        // Remove non-alphanumeric characters for valid Mermaid IDs
        return new string(name.Where(c => char.IsLetterOrDigit(c)).ToArray());
    }

    private void GenerateSummaryTable(StringBuilder sb, List<ControllerInfo> controllers)
    {
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Controller | Total | Anonymous | Authenticated | User Perm | Mod Perm | Admin Perm |");
        sb.AppendLine("|------------|-------|-----------|---------------|-----------|----------|------------|");

        foreach (var controller in controllers)
        {
            var total = controller.Endpoints.Count;
            var anonymous = CountByAuth(controller, AuthLevel.Anonymous);
            var authenticated = CountByAuth(controller, AuthLevel.Authenticated);
            var userPerm = CountByPermissionPrefix(controller, "user.");
            var modPerm = CountByPermissionPrefix(controller, "moderation.");
            var adminPerm = CountByPermissionPrefix(controller, "admin.");

            sb.AppendLine($"| {controller.DisplayName} | {total} | {anonymous} | {authenticated} | {userPerm} | {modPerm} | {adminPerm} |");
        }

        var totals = new
        {
            Total = controllers.Sum(c => c.Endpoints.Count),
            Anonymous = controllers.Sum(c => CountByAuth(c, AuthLevel.Anonymous)),
            Authenticated = controllers.Sum(c => CountByAuth(c, AuthLevel.Authenticated)),
            UserPerm = controllers.Sum(c => CountByPermissionPrefix(c, "user.")),
            ModPerm = controllers.Sum(c => CountByPermissionPrefix(c, "moderation.")),
            AdminPerm = controllers.Sum(c => CountByPermissionPrefix(c, "admin."))
        };

        sb.AppendLine($"| **Total** | **{totals.Total}** | **{totals.Anonymous}** | **{totals.Authenticated}** | **{totals.UserPerm}** | **{totals.ModPerm}** | **{totals.AdminPerm}** |");
        sb.AppendLine();
    }

    private static int CountByAuth(ControllerInfo controller, AuthLevel level)
    {
        return controller.Endpoints.Count(e =>
            e.AuthLevel == level &&
            string.IsNullOrEmpty(e.AttributePermission) &&
            !e.InlineChecks.Any());
    }

    private static int CountByPermissionPrefix(ControllerInfo controller, string prefix)
    {
        return controller.Endpoints.Count(e =>
            e.AllPermissions.Any(p => p.Permission.StartsWith(prefix)));
    }

    private void GenerateDetailedList(StringBuilder sb, List<ControllerInfo> controllers)
    {
        sb.AppendLine("## Detailed Endpoint List");
        sb.AppendLine();

        foreach (var controller in controllers)
        {
            sb.AppendLine($"### {controller.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"Base route: `{controller.RoutePrefix}`");
            sb.AppendLine();

            if (controller.DefaultAuthLevel != AuthLevel.Anonymous ||
                !string.IsNullOrEmpty(controller.DefaultPermission))
            {
                sb.AppendLine($"Controller-level auth: **{FormatAuthLevel(controller.DefaultAuthLevel, controller.DefaultPermission)}**");
                sb.AppendLine();
            }

            sb.AppendLine("| Method | Route | Auth | Permissions |");
            sb.AppendLine("|--------|-------|------|-------------|");

            foreach (var endpoint in controller.Endpoints)
            {
                var route = string.IsNullOrEmpty(endpoint.RouteTemplate) ? "/" : endpoint.RouteTemplate;
                var auth = FormatAuthLevel(endpoint.AuthLevel, endpoint.AttributePermission);
                var permissions = FormatPermissions(endpoint);

                sb.AppendLine($"| {endpoint.HttpMethod} | `{route}` | {auth} | {permissions} |");
            }

            sb.AppendLine();
        }
    }

    private static string FormatAuthLevel(AuthLevel level, string? permission)
    {
        return level switch
        {
            AuthLevel.Anonymous => "Anonymous",
            AuthLevel.Authenticated => "Authenticated",
            AuthLevel.Permission when !string.IsNullOrEmpty(permission) => $"`{permission}`",
            _ => "Unknown"
        };
    }

    private static string FormatPermissions(EndpointInfo endpoint)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(endpoint.AttributePermission))
        {
            parts.Add($"`{endpoint.AttributePermission}`");
        }

        foreach (var check in endpoint.InlineChecks)
        {
            parts.Add($"`{check.Permission}` (runtime)");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }
}
