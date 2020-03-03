using DEM.Net.Core.Configuration;
using DEM.Net.Extension.SketchFab.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace SampleApp
{
    class SketchFabSample
    {
        private readonly ILogger _logger;
        private readonly SketchFabExporter _sketchFabExport;
        private readonly AppSecrets _secrets;

        public SketchFabSample(SketchFabExporter sketchFabExport, ILogger<SketchFabSample> logger, IOptions<AppSecrets> secrets)
        {
            this._logger = logger;
            this._sketchFabExport = sketchFabExport;
            this._secrets = secrets.Value;
        }

        public void Run()
        {
            UploadModelRequest upload = new UploadModelRequest()
            {
                Token = _secrets.SketchFabToken,
                Description = "Helladic test upload",
                FilePath = @"C:\Repos\DEM.Net.Extensions\Samples\bin\Debug\netcoreapp3.1\All_Topo\C5196_ASTER_GDEMV3_OsmMapBox-Outdoors.glb",
                IsInspectable = true,
                IsPrivate = false,
                IsPublished = false,
                Name = "C5196_GDEMV3_Osm_MapBoxOutdoors"
            };


            var uuid = _sketchFabExport.UploadFile(upload).GetAwaiter().GetResult();
        }
    }
}
