namespace Nuuru.Tools.AuthFlowChart.Models;

public enum AuthLevel
{
    Anonymous,      // No authentication required ([AllowAnonymous] or no auth attributes)
    Authenticated,  // Any authenticated user ([Authorize] without policy)
    Permission      // Specific permission required ([Authorize(Policy = "...")])
}
