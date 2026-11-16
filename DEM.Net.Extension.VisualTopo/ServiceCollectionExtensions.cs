using System;
using System.Collections.Generic;
using System.Text;
﻿using DEM.Net.Extension.VisualTopo.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DEM.Net.Extension.VisualTopo;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddDemNetVisualTopoExtension(this IServiceCollection services)
    {
        services
            .AddTransient<IVisualTopoRepository, MemoryRepository>()
            .AddTransient<VisualTopoService>();

        return services;
    }
}
