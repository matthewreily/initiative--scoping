namespace InitiativeScoping.Web.Models;

public static class Paging
{
    public static readonly IReadOnlyList<int> Sizes = [25, 50, 100];

    public static int NormalizeSize(int? size, int fallback) =>
        size is not null && Sizes.Contains(size.Value) ? size.Value : fallback;

    public static int PageCount(int total, int size) => Math.Max(1, (total + size - 1) / size);

    public static int ClampPage(int page, int total, int size) => Math.Clamp(page, 1, PageCount(total, size));

    /// <summary>Page numbers to render around <paramref name="page"/>; null marks an elided run.</summary>
    public static IReadOnlyList<int?> Window(int page, int pageCount, int radius = 2)
    {
        var result = new List<int?>();
        int? last = null;
        for (var p = 1; p <= pageCount; p++)
        {
            if (p != 1 && p != pageCount && Math.Abs(p - page) > radius)
            {
                continue;
            }

            if (last is not null && p - last > 1)
            {
                result.Add(null);
            }

            result.Add(p);
            last = p;
        }

        return result;
    }
}

public sealed record PagerModel(int Page, int PageSize, int Total, Func<int, int, string> Url)
{
    public int PageCount => Paging.PageCount(Total, PageSize);
    public int First => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int Last => Math.Min(Total, Page * PageSize);
}
