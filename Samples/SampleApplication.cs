//
// Program.cs
//
// Author:
//       Xavier Fischer
//
// Copyright (c) 2019 
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using DEM.Net.Core;
using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DEM.Net.Core.Configuration;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace SampleApp
{
    /// <summary>
    /// Main sample application
    /// </summary>
    public class SampleApplication : IHostedService
    {
        private readonly ILogger<SampleApplication> _logger;
        private readonly IRasterService rasterService;
        private readonly OsmExtensionSample osmSample;
        private const string DATA_FILES_PATH = null; //@"C:\Users\ElevationAPI\AppData\Local"; // Leave to null for default location (Environment.SpecialFolder.LocalApplicationData)

        public SampleApplication(ILogger<SampleApplication> logger,
            IRasterService rasterService,
            OsmExtensionSample osmSample)
        {
            _logger = logger;
            this.rasterService = rasterService;
            this.osmSample = osmSample;
        }


        public Task StartAsync(CancellationToken cancellationToken)
        {
            //Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

            Stopwatch sw = Stopwatch.StartNew();
            _logger.LogInformation("Application started");

            bool pauseAfterEachSample = true;

            // Change data dir if not null
            if (!string.IsNullOrWhiteSpace(DATA_FILES_PATH))
            {
                rasterService.SetLocalDirectory(DATA_FILES_PATH);
            }

            
            using (_logger.BeginScope($"Running {nameof(OsmExtensionSample)}.."))
            {
                osmSample.Run();
               
                _logger.LogInformation($"Sample {nameof(OsmExtensionSample)} done. Press any key to run the next sample...");
                if (pauseAfterEachSample) Console.ReadLine();
                if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            }
           
            _logger.LogTrace($"Application ran in : {sw.Elapsed:g}");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace($"Application stopping...");
            return Task.CompletedTask;
        }
    }
}
