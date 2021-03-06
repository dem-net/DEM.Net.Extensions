﻿//
// MemoryRepository.cs
//
// Author:
//       Xavier Fischer 2020-9
//
// Copyright (c) 2020 Xavier Fischer
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
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DEM.Net.Extension.VisualTopo.Storage
{
    public class MemoryRepository : IVisualTopoRepository
    {
        private readonly IMemoryCache memoryCache;
        private readonly VisualTopoOptions options;
        private const string CacheKey = "_TopoModel";

        public MemoryRepository(IMemoryCache memoryCache, IOptions<VisualTopoOptions> options)
        {
            this.memoryCache = memoryCache;
            this.options = options.Value;
        }

        public async Task<Guid> AddModelAsync(VisualTopoModel model)
        {
            Guid id = Guid.NewGuid();

            await memoryCache.GetOrCreateAsync(GetKey(id), entry =>
            {
                entry.SetSlidingExpiration(TimeSpan.FromMinutes(options.MemoryCacheDurationMinutes));
                return Task.FromResult(model);
            });

            return id;
        }

        public Task DeleteModelAsync(Guid id)
        {
            memoryCache.Remove(GetKey(id));
            return Task.CompletedTask;
        }

        public Task<VisualTopoModel> GetModelAsync(Guid id)
        {
            return Task.FromResult(memoryCache.Get<VisualTopoModel>(GetKey(id)));
        }

        public async Task UpdateModelAsync(VisualTopoModel model, Guid id)
        {
            await DeleteModelAsync(id);
            await memoryCache.GetOrCreateAsync(GetKey(id), entry =>
            {
                entry.SetSlidingExpiration(TimeSpan.FromMinutes(options.MemoryCacheDurationMinutes));
                return Task.FromResult(model);
            });
        }

        private string GetKey(Guid id)
        {
            return string.Concat(id, CacheKey);
        }


    }
}
