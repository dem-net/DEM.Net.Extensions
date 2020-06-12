using System.Linq;
using System.Collections.Generic;
using System.Collections;

namespace DEM.Net.Extension.Osm
{
    public class OsmModelList<T> : IEnumerable<T> where T : CommonModel
    {
        public OsmModelList()
        {
            this.Models = new List<T>();
        }
        public OsmModelList(int count)
        {
            this.Models = new List<T>(count);
        }

        public int Count => Models?.Count ?? 0;
        public List<T> Models { get; set; }
        public int TotalPoints { get; set; }

        public IEnumerator<T> GetEnumerator()
        {
            return Models.GetEnumerator();
        }

        internal void Add(T model)
        {
            Models.Add(model);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Models.GetEnumerator();
        }
    }
}
