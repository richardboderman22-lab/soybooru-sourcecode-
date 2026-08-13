using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nuuru.Tools.AuthFlowChart.Models;

namespace Nuuru.Tools.AuthFlowChart.Analysis;

/// <summary>
/// Detects inline permission checks like User.HasPermission() in method bodies
/// </summary>
public class InlineCheckVisitor : CSharpSyntaxWalker
{
    private readonly List<PermissionCheck> _checks = new();

    public IReadOnlyList<PermissionCheck> Checks => _checks;

    public static IReadOnlyList<PermissionCheck> ExtractFromMethod(MethodDeclarationSyntax method)
    {
        var visitor = new InlineCheckVisitor();
        visitor.Visit(method.Body);
        visitor.Visit(method.ExpressionBody);
        return visitor.Checks;
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Look for User.HasPermission() or .HasPermission() calls
        if (node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;

            if (methodName == "HasPermission" || methodName == "HasClaim")
            {
                var permission = ExtractPermissionArgument(node, methodName);
                if (!string.IsNullOrEmpty(permission))
                {
                    _checks.Add(new PermissionCheck(permission, PermissionSource.Inline));
                }
            }
        }

        base.VisitInvocationExpression(node);
    }

    private static string? ExtractPermissionArgument(InvocationExpressionSyntax invocation, string methodName)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return null;

        // For HasPermission, the first argument is the permission
        // For HasClaim, the second argument is the permission value (first is claim type)
        var argIndex = methodName == "HasClaim" && args.Count >= 2 ? 1 : 0;
        if (argIndex >= args.Count)
            return null;

        var arg = args[argIndex].Expression;

        // Handle string literal: "user.upload_post"
        if (arg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        // Handle permission constant: Permissions.User.UploadPost
        if (arg is MemberAccessExpressionSyntax memberAccess)
        {
            var fullName = memberAccess.ToString();
            return ResolvePermissionConstant(fullName);
        }

        // Handle identifier (variable containing permission)
        if (arg is IdentifierNameSyntax identifier)
        {
            // We can't resolve variable values statically, but we note it
            return $"[{identifier.Identifier.Text}]";
        }

        return null;
    }

    /// <summary>
    /// Resolve permission constants like "Permissions.User.UploadPost" to their string values
    /// </summary>
    private static string ResolvePermissionConstant(string value)
    {
        // If it's already a string value, return it
        if (!value.StartsWith("Permissions."))
            return value;

        // Map known permission constants to their values
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
            _ => value
        };
    }
}
