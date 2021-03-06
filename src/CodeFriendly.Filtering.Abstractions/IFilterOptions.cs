using System.Collections.Generic;

namespace CodeFriendly.Filtering.Abstractions
{
    public interface IFilterOptions
    {
        string Expression { get; set; }
        IEnumerable<ISort> Sorts { get; set; }
        
        int? Limit { get; set; }
        int? OffSet { get; set; }

        
    }
}