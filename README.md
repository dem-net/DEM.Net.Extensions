# DEM.Net.Extensions

Home of various extensions for DEM.Net.
Not well documented but deserved to go public. Please fill an issue if you need help.

## Sketchfab Samples

[SketchfabApi.Net](https://github.com/dem-net/SketchFabApi.Net) should be cloned in the root directory aside this repo to get this working.

```
-Repos
|_ Dem.Net.Extensions
|_ SketchfabApi.Net
```
## OSM

3D buildings, roads, railways and water for DEM.Net models, read from a local
[Protomaps basemap](https://maps.protomaps.com/builds/) PMTiles archive (`OsmElevationOptions.PMTilesFilePath`),
draped on the terrain.

Buildings can come from the [Overture Maps buildings archive](https://docs.overturemaps.org/examples/overture-tiles/)
instead (`OsmElevationOptions.BuildingsPMTilesFilePath`, `tiles/<release>/buildings.pmtiles`, about 181 GB for the
planet): it keeps the heights, floors, roof shapes and colours that the Protomaps archive drops. Regional extract:
`pmtiles extract https://overturemaps-extras-us-west-2.s3.us-west-2.amazonaws.com/tiles/2026-11-18.0/buildings.pmtiles out.pmtiles --bbox=…`

## SampleApp

Various samples, one including model generation for http://helladic.info (gallery here: sketchfab.com/mycenaean-atlas-project)
. Needs SketchFab repo.

### TODO

- Better triangulations
- Better docs
- Your ideas...
