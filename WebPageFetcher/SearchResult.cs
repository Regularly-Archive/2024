using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebPageFetcher
{
    public class SearchResult
    {
        public List<Entry> Entries { get; set; } = new List<Entry>();
    }

    public class Entry
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }
}
