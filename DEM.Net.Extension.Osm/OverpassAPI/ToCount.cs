using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DEM.Net.Extension.Osm.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DEM.Net.Extension.Osm.OverpassAPI
{

    /// <summary>
    /// The JSON result of an Overpass query.
    /// </summary>
    public static partial class OverpassAPIExtentions
    {

        public static Task<OverpassCountResult> ToCountAsync(this Task<OverpassResult> ResultTask)
        {
            return ResultTask.ContinueWith(task =>
            {
                var result = ResultTask.Result;
                if (result.Elements!=null)
                    return result.Elements.First().ToObject<OverpassCountResult>();
                else
                    return new OverpassCountResult();
            }, TaskScheduler.Default);
        }
    }
}
