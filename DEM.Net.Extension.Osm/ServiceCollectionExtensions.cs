using System;
using System.Collections.Generic;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.Highways;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DEM.Net.Extension.Osm;


public static class ServiceCollectionExtension
{
    public static IServiceCollection AddDemNetOsmExtension(this IServiceCollection services)
    {
        services
            .AddTransient<IOsmDataServiceFactory, OsmDataServiceFactory>()

            .AddSingleton<PMTilesReaderProvider>()
            .AddTransient<PMTilesDataService>()
            .AddSingleton<Func<PMTilesDataService>>(x => x.GetService<PMTilesDataService>)

            .AddTransient<DefaultOsmProcessor>();
        //services.AddTransient<IOsmDataService, OsmDataServiceVectorTiles>()
        //        .AddTransient<DefaultOsmProcessor>();


        return services;
    }
}
