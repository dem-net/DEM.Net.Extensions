namespace DEM.Net.Extension.Osm
{
    public class BasicOsmDataFilter : IOsmDataFilter
    {
        public string[] WaysFilter { get; set; }
        public string[] RelationsFilter { get; set; }
        public string[] NodesFilter { get; set; }

        public static IOsmDataFilter Create(string[] waysFilter, string[] relationsFilter = null, string[] nodesFilter = null)
        {
            return new BasicOsmDataFilter { NodesFilter = nodesFilter, WaysFilter = waysFilter, RelationsFilter = relationsFilter };
        }
    }

}
