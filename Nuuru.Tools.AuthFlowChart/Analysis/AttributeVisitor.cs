using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nuuru.Tools.AuthFlowChart.Models;

namespace Nuuru.Tools.AuthFlowChart.Analysis;

/// <summary>
/// Extracts authorization-related attributes from classes and methods
/// </summary>
public class AttributeVisitor : CSharpSyntaxWalker
{
    public string? RoutePrefix { get; private set; }
    public AuthLevel AuthLevel { get; private set; } = AuthLevel.Anonymous;
    public string? PolicyPermission { get; private set; }
    public bool HasAuthorize { get; private set; }
    public bool HasAllowAnonymous { get; private set; }

    public static (AuthLevel level, string? permission, string? route) ExtractFromClass(ClassDeclarationSyntax classDecl)
    {
        var visitor = new AttributeVisitor();
        visitor.VisitClassAttributes(classDecl);
        return (visitor.AuthLevel, visitor.PolicyPermission, visitor.RoutePrefix);
    }

    public static (AuthLevel level, string? permission, string? httpMethod, string? route) ExtractFromMethod(MethodDeclarationSyntax method)
    {
        var visitor = new AttributeVisitor();
        visitor.VisitMethodAttributes(method);
        return (visitor.AuthLevel, visitor.PolicyPermission, visitor.GetHttpMethod(method), visitor.RoutePrefix);
    }

    private void VisitClassAttributes(ClassDeclarationSyntax classDecl)
    {
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                ProcessAttribute(attr);
            }
        }
    }

    private void VisitMethodAttributes(MethodDeclarationSyntax method)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                ProcessAttribute(attr);
            }
        }
    }

    private void ProcessAttribute(AttributeSyntax attr)
    {
        var name = GetAttributeName(attr);

        switch (name)
        {
            case "Route":
            case "RouteAttribute":
                RoutePrefix = ExtractFirstStringArgument(attr);
                break;

            case "AllowAnonymous":
            case "AllowAnonymousAttribute":
                HasAllowAnonymous = true;
                AuthLevel = AuthLevel.Anonymous;
                PolicyPermission = null;
                break;

            case "Authorize":
            case "AuthorizeAttribute":
                if (!HasAllowAnonymous) // AllowAnonymous overrides Authorize
                {
                    HasAuthorize = true;
                    var policy = ExtractPolicyArgument(attr);
                    if (!string.IsNullOrEmpty(policy))
                    {
                        AuthLevel = AuthLevel.Permission;
                        PolicyPermission = ResolvePermissionConstant(policy);
                    }
                    else
                    {
                        AuthLevel = AuthLevel.Authenticated;
                    }
                }
                break;

            case "HttpGet":
            case "HttpGetAttribute":
            case "HttpPost":
            case "HttpPostAttribute":
            case "HttpPut":
            case "HttpPutAttribute":
            case "HttpDelete":
            case "HttpDeleteAttribute":
            case "HttpPatch":
            case "HttpPatchAttribute":
                // Extract route template if present
                var routeArg = ExtractFirstStringArgument(attr);
                if (!string.IsNullOrEmpty(routeArg))
                {
                    RoutePrefix = routeArg;
                }
                break;
        }
    }

    private string GetHttpMethod(MethodDeclarationSyntax method)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = GetAttributeName(attr);
                if (name.StartsWith("Http"))
                {
                    return name.Replace("Attribute", "").Replace("Http", "").ToUpperInvariant();
                }
            }
        }
        return "GET"; // Default
    }

    private static string GetAttributeName(AttributeSyntax attr)
    {
        var name = attr.Name.ToString();
        // Handle qualified names like Microsoft.AspNetCore.Authorization.Authorize
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            name = name[(lastDot + 1)..];
        }
        return name;
    }

    private static string? ExtractFirstStringArgument(AttributeSyntax attr)
    {
        if (attr.ArgumentList == null || attr.ArgumentList.Arguments.Count == 0)
            return null;

        var firstArg = attr.ArgumentList.Arguments[0];
        if (firstArg.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }
        return null;
    }

    private static string? ExtractPolicyArgument(AttributeSyntax attr)
    {
        if (attr.ArgumentList == null)
            return null;

        foreach (var arg in attr.ArgumentList.Arguments)
        {
            // Look for Policy = "..."
            if (arg.NameEquals?.Name.Identifier.Text == "Policy")
            {
                if (arg.Expression is LiteralExpressionSyntax literal)
                {
                    return literal.Token.ValueText;
                }
                // Handle Permissions.User.UploadPost reference
                if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.ToString();
                }
            }

            // Also handle positional argument if it's the policy
            if (arg.NameEquals == null && arg.NameColon == null)
            {
                if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.ToString();
                }
                if (arg.Expression is LiteralExpressionSyntax literal)
                {
                    return literal.Token.ValueText;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Resolve permission constants like "Permissions.User.UploadPost" to their string values
    /// </summary>
    private static string ResolvePermissionConstant(string value)
    {
        // If it's already a string value (e.g., "user.upload_post"), return it
        if (!value.StartsWith("Permissions."))
            return value;

        // Map known permission constants to their values
        // This is populated from the Permissions.cs file
        return value switch
        {
            "Permissions.User.UploadPost" => "user.upload_post",
            "Permissions.User.Comment" => "user.comment",
            "Permissions.User.EditOwnContent" => "user.edit_own_content",
            "Permissions.User.DeleteOwnContent" => "user.delete_own_content",
            "Permissions.User.EditTags" => "user.edit_tags",
            "Permissions.Moderation.TrashPost" => "moderation.trash_post",
            "Permissions.Moderation.DeleteComment" => "moderation.delete_comment",
            "Permissions.Moderation.EditTags" => "moderation.edit_tags",
            "Permissions.Moderation.BanUser" => "moderation.ban_user",
            "Permissions.Moderation.ViewReports" => "moderation.view_reports",
            "Permissions.Moderation.ViewAuditLog" => "moderation.view_audit_log",
            "Permissions.Admin.ManageUsers" => "admin.manage_users",
            "Permissions.Admin.ManagePermissions" => "admin.manage_permissions",
            "Permissions.Admin.SystemSettings" => "admin.system_settings",
            _ => value // Return as-is if not found
        };
    }
}
