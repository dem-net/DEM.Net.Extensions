using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.Highways;
using Microsoft.Extensions.Configuration;
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


#if NET5_0
                .AddTransient<TiledFlatGeobufDataService>()
                .AddSingleton<Func<TiledFlatGeobufDataService>>(x => x.GetService<TiledFlatGeobufDataService>)
#endif


                .AddTransient<DefaultOsmProcessor>();
            //services.AddTransient<IOsmDataService, OsmDataServiceVectorTiles>()
            //        .AddTransient<DefaultOsmProcessor>();


            return services;
        }
    }

}
