namespace Frontend.Domain.DTOs.Maintenance;

// SCRUM-174/175: one backend service and whether an admin has switched it off
// (EdgeService GET /api/edge/maintenance). Service is the route name, e.g. "lobbyservice".
public sealed record MaintenanceStatusDto(
    string Service,
    string Name,
    bool Off,
    string? Message,
    string? By,
    DateTimeOffset? Since);

// PUT /api/edge/maintenance/{service} (Admin only).
public sealed record SwitchMaintenanceRequest(bool Off, string? Message);
