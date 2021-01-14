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

                .AddTransient<OsmDataServiceFlatGeobuf>()
                .AddSingleton<Func<OsmDataServiceFlatGeobuf>>(x => x.GetService<OsmDataServiceFlatGeobuf>)


                .AddTransient<DefaultOsmProcessor>();
            //services.AddTransient<IOsmDataService, OsmDataServiceVectorTiles>()
            //        .AddTransient<DefaultOsmProcessor>();


            return services;
        }
    }

}
