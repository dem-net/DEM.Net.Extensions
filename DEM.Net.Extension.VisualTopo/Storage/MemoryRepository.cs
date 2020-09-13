//
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
using Microsoft.Extensions.Caching.Memory;

namespace DEM.Net.Extension.VisualTopo.Storage
{
    public class MemoryRepository : IBasicRepository
    {
        private readonly IMemoryCache memoryCache;
        private const string CacheKey = "_TopoModel";
        private readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(1);

        public MemoryRepository(IMemoryCache memoryCache)
        {
            this.memoryCache = memoryCache;
        }

        public Guid AddModel(VisualTopoModel model)
        {
            Guid id = Guid.NewGuid();

            memoryCache.GetOrCreate(GetKey(id), entry =>
            {
                entry.SetSlidingExpiration(SlidingExpiration);
                return model;
            });

            return id;
        }

        public void DeleteModel(Guid id)
        {
            memoryCache.Remove(GetKey(id));
        }

        public VisualTopoModel GetModel(Guid id)
        {
            return memoryCache.Get<VisualTopoModel>(GetKey(id));
        }

        public void UpdateModel(VisualTopoModel model, Guid id)
        {
            DeleteModel(id);
            memoryCache.GetOrCreate(GetKey(id), entry =>
            {
                entry.SetSlidingExpiration(SlidingExpiration);
                return model;
            });
        }

        private string GetKey(Guid id)
        {
            return string.Concat(id, CacheKey);
        }


    }
}
