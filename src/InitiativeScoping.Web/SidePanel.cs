namespace InitiativeScoping.Web;

/// <summary>Edit pages can be loaded into the side panel on Initiative Details; the client marks such requests with a header.</summary>
public static class SidePanel
{
    public const string Header = "X-Panel";
    public const string Layout = "_PanelLayout";

    public static bool IsPanelRequest(HttpRequest request) => request.Headers[Header] == "1";
}
