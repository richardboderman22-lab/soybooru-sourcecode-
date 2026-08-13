using Nuuru.Tools.AuthFlowChart.Analysis;
using Nuuru.Tools.AuthFlowChart.Output;

// Parse command line arguments
string controllersPath;
string outputPath;

if (args.Length >= 1)
{
    controllersPath = args[0];
}
else
{
    // Default: look for Nuuru.Server/Controllers relative to current directory
    var currentDir = Directory.GetCurrentDirectory();
    controllersPath = Path.Combine(currentDir, "Nuuru.Server", "Controllers");

    // If not found, try going up one level (in case running from tool directory)
    if (!Directory.Exists(controllersPath))
    {
        controllersPath = Path.Combine(currentDir, "..", "Nuuru.Server", "Controllers");
    }
}

outputPath = args.Length >= 2 ? args[1] : "auth-flowchart.md";

// Validate controllers path
if (!Directory.Exists(controllersPath))
{
    Console.Error.WriteLine($"Error: Controllers directory not found: {controllersPath}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage: Nuuru.Tools.AuthFlowChart [controllers-path] [output-path]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Arguments:");
    Console.Error.WriteLine("  controllers-path  Path to the Controllers directory (default: Nuuru.Server/Controllers)");
    Console.Error.WriteLine("  output-path       Output markdown file path (default: auth-flowchart.md)");
    return 1;
}

Console.WriteLine($"Analyzing controllers in: {Path.GetFullPath(controllersPath)}");

// Analyze
var analyzer = new ControllerAnalyzer();
var controllers = analyzer.Analyze(controllersPath);

if (controllers.Count == 0)
{
    Console.Error.WriteLine("No controllers found.");
    return 1;
}

// Generate output
var generator = new MermaidGenerator();
generator.GenerateMarkdownFile(controllers, outputPath);

// Summary
var totalEndpoints = controllers.Sum(c => c.Endpoints.Count);
var totalInlineChecks = controllers.Sum(c => c.Endpoints.Sum(e => e.InlineChecks.Count));

Console.WriteLine();
Console.WriteLine($"Generated: {Path.GetFullPath(outputPath)}");
Console.WriteLine();
Console.WriteLine("Summary:");
Console.WriteLine($"  Controllers: {controllers.Count}");
Console.WriteLine($"  Endpoints: {totalEndpoints}");
Console.WriteLine($"  Inline permission checks: {totalInlineChecks}");
Console.WriteLine();
Console.WriteLine("Controllers analyzed:");
foreach (var controller in controllers)
{
    var inlineCount = controller.Endpoints.Sum(e => e.InlineChecks.Count);
    var inlineSuffix = inlineCount > 0 ? $" ({inlineCount} inline checks)" : "";
    Console.WriteLine($"  - {controller.Name}: {controller.Endpoints.Count} endpoints{inlineSuffix}");
}

return 0;
