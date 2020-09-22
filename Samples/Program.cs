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
using System.IO;
using System;
using Microsoft.Extensions.DependencyInjection;
using DEM.Net.glTF;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Configuration;
using DEM.Net.Core.Configuration;
using System.Threading.Tasks;
using DEM.Net.Extension.Osm;
using DEM.Net.Extension.VisualTopo;

namespace SampleApp
{

    /// <summary>
    /// Console program entry point. This is boilerplate code for .Net Core Console logging and DI
    /// except for the RegisterSamples() where samples are registered
    /// 
    /// Here we configure logging and services (via dependency injection)
    /// And setup and run the main Application
    /// </summary>
    class Program
    {

        static async Task Main(string[] args)
        {

            // Load config
            // Load appsettings.json
            var config = LoadAppSettings();
            if (null == config)
            {
                Console.WriteLine("Missing or invalid appsettings.json file.");
                return;
            }

            // Setting up dependency injection
            var serviceCollection = new ServiceCollection();
            ConfigureServices(config, serviceCollection);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            serviceProvider.GetRequiredService<SampleApplication>().Run();

        }

        private static IConfigurationRoot LoadAppSettings()
        {
            try
            {

                var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                //.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                .AddJsonFile("secrets.json", optional: true, reloadOnChange: false)
                .Build();

                return config;
            }
            catch (System.IO.FileNotFoundException)
            {
                return null;
            }
        }


        private static void ConfigureServices(IConfigurationRoot appConfig, IServiceCollection services)
        {
            services
            .AddLogging(config =>
            {
                // clear out default configuration
                config.ClearProviders();

                config.AddConfiguration(appConfig.GetSection("Logging"));
                config.AddDebug();
                config.AddConsole(o =>
                        {
                            o.IncludeScopes = false;
                            o.DisableColors = false;
                        }); // Log to console (colored !)
            })
           //.Configure<LoggerFilterOptions>(options =>
           //{
           //    options.AddFilter<DebugLoggerProvider>(null /* category*/ , LogLevel.Information /* min level */);
           //    options.AddFilter<ConsoleLoggerProvider>(null  /* category*/ , LogLevel.Information /* min level */);
           //    options.AddFilter<ConsoleLoggerProvider>("System.Net.Http.HttpClient", LogLevel.Warning);

           //    // Comment this line to see all internal DEM.Net logs
           //    //options.AddFilter<ConsoleLoggerProvider>("DEM.Net", LogLevel.Information);
           //})
           .Configure<AppSecrets>(appConfig.GetSection(nameof(AppSecrets)))
           .Configure<DEMNetOptions>(appConfig.GetSection(nameof(DEMNetOptions)))
           .AddDemNetCore()
           .AddDemNetglTF()
           .AddDemNetOsmExtension()
           .AddDemNetVisualTopoExtension();

            SampleApplication.RegisterSamples(services);


        }

    }
}
