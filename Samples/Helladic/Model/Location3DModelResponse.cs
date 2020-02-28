using DEM.Net.Core;
using System.Collections.Generic;

namespace SampleApp
{
    #region Model
    public class Location3DModelResponse
    {
        public string Id { get; set; }
        public Location3DModelRequest Request { get; set; }
        public Location3DModelSettings Settings { get; set; }
        public string ModelFile { get; set; }

        public List<Attribution> Attributions { get; set; } = new List<Attribution>();

    }

    #endregion
}
