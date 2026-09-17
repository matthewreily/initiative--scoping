namespace InitiativeScoping.Web.Models;

public record SearchHit(int Id, string Title, string Subtitle);

public class SearchResultsModel
{
    public string Query { get; set; } = "";
    public IReadOnlyList<SearchHit> Initiatives { get; set; } = [];
    public IReadOnlyList<SearchHit> People { get; set; } = [];
    public IReadOnlyList<SearchHit> RateCards { get; set; } = [];
    public IReadOnlyList<SearchHit> Vendors { get; set; } = [];
    public int Total => Initiatives.Count + People.Count + RateCards.Count + Vendors.Count;
}
