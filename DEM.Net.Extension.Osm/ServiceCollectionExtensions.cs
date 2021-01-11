using DEM.Net.Extension.Osm.Buildings;
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

                .AddTransient<OsmServiceOverpassAPI>()
                .AddSingleton<Func<OsmServiceOverpassAPI>>(x => x.GetService<OsmServiceOverpassAPI>)

                .AddTransient<OsmDataServiceVectorTiles>()
                .AddSingleton<Func<OsmDataServiceVectorTiles>>(x => x.GetService<OsmDataServiceVectorTiles>)


                .AddTransient<DefaultOsmProcessor>();
            //services.AddTransient<IOsmDataService, OsmDataServiceVectorTiles>()
            //        .AddTransient<DefaultOsmProcessor>();


            return services;
        }
    }

}
