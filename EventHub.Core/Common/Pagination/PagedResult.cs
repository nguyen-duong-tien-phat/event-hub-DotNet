namespace EventHub.Core.Common;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    
    public PagedResult<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return new PagedResult<TNew>
        {
            Items = Items.Select(mapper).ToList(),
            Page = Page,
            PageSize = PageSize,
            TotalCount = TotalCount
        };
    }
}