using DEM.Net.Core.Configuration;
using DEM.Net.Extension.SketchFab.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
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
                FilePath = Path.Combine(Directory.GetCurrentDirectory(), "SketchFab", "C1410_Satellite.glb"),
                IsInspectable = true,
                IsPrivate = false,
                IsPublished = false,
                Name = "C1410_Satellite"
            };


            var uuid = _sketchFabExport.UploadFile(upload).GetAwaiter().GetResult();
        }
    }
}
