﻿using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.Highways;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace DEM.Net.Extension.Osm
{

    public static class ServiceCollectionExtension
    {
        public static IServiceCollection AddDemNetOsmExtension(this IServiceCollection services)
        {
            
            services
                .AddTransient<IOsmDataServiceFactory, OsmDataServiceFactory>()

                .AddTransient<OverpassAPIDataService>()
                .AddSingleton<Func<OverpassAPIDataService>>(x => x.GetService<OverpassAPIDataService>)

                .AddTransient<TiledFlatGeobufDataService>()
                .AddSingleton<Func<TiledFlatGeobufDataService>>(x => x.GetService<TiledFlatGeobufDataService>)


                .AddTransient<DefaultOsmProcessor>();
            //services.AddTransient<IOsmDataService, OsmDataServiceVectorTiles>()
            //        .AddTransient<DefaultOsmProcessor>();


            return services;
        }
    }

}
