namespace InitiativeScoping.Web.Models;

public sealed record StatusViewModel(int Code, string RequestId)
{
    public string Title => Code switch
    {
        403 => "Access denied",
        404 => "Not found",
        413 => "Request too large",
        _ => "Something went wrong"
    };

    public string Detail => Code switch
    {
        403 => "Your role does not permit this action. Contact an administrator if you believe you should have access.",
        404 => "The page or record you requested does not exist or has been removed.",
        413 => "The uploaded file exceeds the maximum allowed size.",
        _ => "The request could not be completed."
    };
}
