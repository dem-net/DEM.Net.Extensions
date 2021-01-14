namespace DEM.Net.Extension.Osm
{
    public class BasicOsmDataFilter : IOsmDataSettings
    {
        public string[] WaysFilter { get; set; }
        public string[] RelationsFilter { get; set; }
        public string[] NodesFilter { get; set; }
        public string FlatGeobufTilesDirectory => null;

        public static IOsmDataSettings Create(string[] waysFilter, string[] relationsFilter = null, string[] nodesFilter = null)
        {
            return new BasicOsmDataFilter { NodesFilter = nodesFilter, WaysFilter = waysFilter, RelationsFilter = relationsFilter };
        }
    }

}
