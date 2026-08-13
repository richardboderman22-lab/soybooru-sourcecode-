using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nuuru.Tools.AuthFlowChart.Models;

namespace Nuuru.Tools.AuthFlowChart.Analysis;

/// <summary>
/// Main analyzer that processes controller files and extracts auth information
/// </summary>
public class ControllerAnalyzer
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch",
        "HttpGetAttribute", "HttpPostAttribute", "HttpPutAttribute", "HttpDeleteAttribute", "HttpPatchAttribute"
    };

    public List<ControllerInfo> Analyze(string controllersDirectory)
    {
        var controllers = new List<ControllerInfo>();

        if (!Directory.Exists(controllersDirectory))
        {
            Console.Error.WriteLine($"Directory not found: {controllersDirectory}");
            return controllers;
        }

        var files = Directory.GetFiles(controllersDirectory, "*Controller.cs");

        foreach (var file in files)
        {
            var controller = AnalyzeFile(file);
            if (controller != null)
            {
                controllers.Add(controller);
            }
        }

        return controllers.OrderBy(c => c.Name).ToList();
    }

    private ControllerInfo? AnalyzeFile(string filePath)
    {
        var code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();

        // Find the controller class
        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => IsController(c));

        if (classDecl == null)
            return null;

        var controller = new ControllerInfo
        {
            Name = classDecl.Identifier.Text
        };

        // Extract class-level attributes
        var (classAuthLevel, classPermission, classRoute) = AttributeVisitor.ExtractFromClass(classDecl);
        controller.DefaultAuthLevel = classAuthLevel;
        controller.DefaultPermission = classPermission;

        // Resolve [controller] token in route
        var resolvedRoute = classRoute ?? $"api/{controller.DisplayName.ToLowerInvariant()}";
        resolvedRoute = resolvedRoute.Replace("[controller]", controller.DisplayName.ToLowerInvariant());
        controller.RoutePrefix = resolvedRoute;

        // Find all action methods (public methods with [Http*] attributes)
        var methods = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(m => IsActionMethod(m));

        foreach (var method in methods)
        {
            var endpoint = AnalyzeMethod(method, controller);
            controller.Endpoints.Add(endpoint);
        }

        return controller;
    }

    private static bool IsController(ClassDeclarationSyntax classDecl)
    {
        // Check if it inherits from ControllerBase or has [ApiController]
        var hasApiControllerAttribute = classDecl.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString().Contains("ApiController"));

        var inheritsFromControllerBase = classDecl.BaseList?.Types
            .Any(t => t.ToString().Contains("Controller")) ?? false;

        return hasApiControllerAttribute || inheritsFromControllerBase;
    }

    private static bool IsActionMethod(MethodDeclarationSyntax method)
    {
        // Check if the method is public
        var isPublic = method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));

        // Check if the method has any [Http*] attribute
        var hasHttpAttribute = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => HttpMethods.Any(h =>
                a.Name.ToString().Equals(h, StringComparison.OrdinalIgnoreCase) ||
                a.Name.ToString().Equals(h.Replace("Attribute", ""), StringComparison.OrdinalIgnoreCase)));

        return isPublic && hasHttpAttribute;
    }

    private EndpointInfo AnalyzeMethod(MethodDeclarationSyntax method, ControllerInfo controller)
    {
        var (methodAuthLevel, methodPermission, httpMethod, methodRoute) = AttributeVisitor.ExtractFromMethod(method);
        var inlineChecks = InlineCheckVisitor.ExtractFromMethod(method);

        var endpoint = new EndpointInfo
        {
            MethodName = method.Identifier.Text,
            HttpMethod = httpMethod ?? "GET",
            RouteTemplate = methodRoute ?? "",
            InlineChecks = inlineChecks.ToList()
        };

        // Determine effective auth level
        // Method-level attributes override controller-level
        var hasMethodAuthAttribute = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a =>
            {
                var name = a.Name.ToString();
                return name.Contains("Authorize") || name.Contains("AllowAnonymous");
            });

        if (hasMethodAuthAttribute)
        {
            endpoint.AuthLevel = methodAuthLevel;
            endpoint.AttributePermission = methodPermission;
            endpoint.InheritsFromController = false;
        }
        else
        {
            // Inherit from controller
            endpoint.AuthLevel = controller.DefaultAuthLevel;
            endpoint.AttributePermission = controller.DefaultPermission;
            endpoint.InheritsFromController = true;
        }

        return endpoint;
    }
}
