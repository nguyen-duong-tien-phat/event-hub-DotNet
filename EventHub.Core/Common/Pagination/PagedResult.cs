namespace EventHub.Core.Common;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Pass it as <c>cursor</c> to get the page after this one; null on the last page.</summary>
    public string? NextCursor { get; set; }

    /// <summary>
    /// Builds a page from rows fetched with one extra: the extra row only proves another page exists (and is dropped).
    /// </summary>
    public static PagedResult<T> From(List<T> fetched, int page, int pageSize, int totalCount, Func<T, string> cursorOf) {
        var more = fetched.Count > pageSize;
        var items = more ? fetched.GetRange(0, pageSize) : fetched;
        return new PagedResult<T> {
            Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount,
            NextCursor = more ? cursorOf(items[^1]) : null,
        };
    }
    
    public PagedResult<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return new PagedResult<TNew>
        {
            Items = Items.Select(mapper).ToList(),
            Page = Page,
            PageSize = PageSize,
            TotalCount = TotalCount,
            NextCursor = NextCursor
        };
    }
}