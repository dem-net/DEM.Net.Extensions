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

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DEM.Net.Core;
using DEM.Net.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sketchfab;

namespace SampleApp;

/// <summary>
/// Main sample application
/// </summary>
public class SampleApplication
{
    private readonly ILogger<SampleApplication> _logger;
    private readonly RasterService rasterService;
    private readonly IServiceProvider services;
    private const string DATA_FILES_PATH = null; //@"C:\Users\ElevationAPI\AppData\Local"; // Leave to null for default location (Environment.SpecialFolder.LocalApplicationData)

    public SampleApplication(ILogger<SampleApplication> logger, IServiceProvider services,
        RasterService rasterService)
    {
        _logger = logger;
        this.rasterService = rasterService;
        this.services = services;

        // Change data dir if not null
        if (!string.IsNullOrWhiteSpace(DATA_FILES_PATH))
        {
            rasterService.SetLocalDirectory(DATA_FILES_PATH);
        }
    }
    internal static void RegisterSamples(IServiceCollection services)
    {
        services.AddScoped<HelladicSample>()
                .AddScoped<Sketchfab.SketchfabApi>()
                .AddScoped<SketchfabSample>()
                .AddScoped<VisualTopoSample>();
        // .. more samples here

        services.AddScoped<SampleApplication>();
    }


    public async Task RunAsync()
    {
        _logger.LogInformation("OnStarted has been called.");

        try
        {
            using (TimeSpanBlock timer = new TimeSpanBlock(nameof(VisualTopoSample), _logger))
            {
                await services.GetService<VisualTopoSample>()!.RunAsync();
            }


            //using (TimeSpanBlock timer = new TimeSpanBlock(nameof(HelladicSample), _logger))
            //{
            //    await services.GetService<HelladicSample>().RunAsync();
            //}

            Debugger.Break();

            //using (TimeSpanBlock timer = new TimeSpanBlock(nameof(SketchfabSample), _logger))
            //{
            //    await services.GetService<SketchfabSample>().RunAsync();
            //}

            //Debugger.Break();

        }
        catch (Exception ex)
        {
            _logger.LogError($"Error: {ex.ToString()}");

        }

    }

}
