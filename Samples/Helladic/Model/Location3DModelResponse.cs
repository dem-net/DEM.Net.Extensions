using System;
using System.Collections.Generic;
using DEM.Net.Core;

namespace SampleApp;

#region Model
public class Location3DModelResponse
{
    public UploadStatus UploadStatus { get; set; } = UploadStatus.None;

    public string Id { get; set; } = null!;
    public Location3DModelRequest Request { get; set; } = null!;
    public Location3DModelSettings Settings { get; set; } = null!;
    public string? UploadedFileId { get; set; }

    public List<Attribution> Attributions { get; set; } = [];
    public TimeSpan Elapsed { get; internal set; }
    public int? NumTiles { get; internal set; }
    public GeoPoint Origin { get; internal set; }
    public IEnumerable<Location3DModelRequest>? ClosebySites { get; internal set; }
}

#endregion
