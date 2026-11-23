namespace Reqviora.Domain.Entities;

public static class ProjectMemberRoles
{
    public const string Viewer = "Viewer";
    public const string Editor = "Editor";

    public static bool IsSupported(string? role) =>
        string.Equals(role, Viewer, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, Editor, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string role) =>
        string.Equals(role, Editor, StringComparison.OrdinalIgnoreCase) ? Editor : Viewer;
}
