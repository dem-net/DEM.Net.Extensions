﻿//
// TINGeneration.cs
//
// Author:
//       Xavier Fischer, Frédéric Aubin
//
// Copyright (c) 2019 
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

using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using DEM.Net.Core.Services.Lab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DEM.Net.glTF.SharpglTF;
using SharpGLTF.Schema2;

namespace SampleApp
{
    public static class TINGeneration
    {
        public static Triangulation GenerateTIN(HeightMap hMap, double precision, int srid)
        {
            var v_pointsToTest = GetGeoPointsByHMap(hMap, srid);


            var _paramTin = FLabServices.createCalculMedium().GetParametresDuTinParDefaut();
            _paramTin.p11_initialisation_determinationFrontieres = enumModeDelimitationFrontiere.pointsProchesDuMbo;
            _paramTin.p12_extensionSupplementaireMboEnM = 0;
            _paramTin.p13_modeCalculZParDefaut = enumModeCalculZ.alti_0;
            _paramTin.p14_altitudeParDefaut = -200;
            _paramTin.p15_nbrePointsSupplMultiples4 = 0;
            _paramTin.p16_initialisation_modeChoixDuPointCentral.p01_excentrationMinimum = 0;
            _paramTin.p21_enrichissement_modeChoixDuPointCentral.p01_excentrationMinimum = precision;

            //
            var _topolFacettes = FLabServices.createCalculMedium().GetInitialisationTin(v_pointsToTest, _paramTin);
            FLabServices.createCalculMedium().AugmenteDetailsTinByRef(ref _topolFacettes, _paramTin);


            Dictionary<int, int> v_indiceParIdPoint = new Dictionary<int, int>();
            int v_indice = 0;
            GeoPoint v_geoPoint;
            List<GeoPoint> p00_geoPoint = new List<GeoPoint>(_topolFacettes.p11_pointsFacettesByIdPoint.Count);
            List<List<int>> p01_listeIndexPointsfacettes = new List<List<int>>(_topolFacettes.p13_facettesById.Count);

            foreach (BeanPoint_internal v_point in _topolFacettes.p11_pointsFacettesByIdPoint.Values)
            {
                v_geoPoint = new GeoPoint(v_point.p10_coord[1], v_point.p10_coord[0], (float)v_point.p10_coord[2]);
                p00_geoPoint.Add(v_geoPoint);
                v_indiceParIdPoint.Add(v_point.p00_id, v_indice);
                v_indice++;
            }
            p00_geoPoint = p00_geoPoint.ToList();


            //Création des listes d'indices et normalisation du sens des points favettes
            List<int> v_listeIndices;
            bool v_renvoyerNullSiPointsColineaires_vf = true;
            bool v_normalisationSensHoraireSinonAntihoraire = false;


            foreach (BeanFacette_internal v_facette in _topolFacettes.p13_facettesById.Values)
            {
                List<BeanPoint_internal> v_normalisationDuSens = FLabServices.createCalculMedium().GetOrdonnancementPointsFacette(v_facette.p01_pointsDeFacette, v_renvoyerNullSiPointsColineaires_vf, v_normalisationSensHoraireSinonAntihoraire);
                if (v_normalisationDuSens != null)
                {
                    v_listeIndices = new List<int>();
                    foreach (BeanPoint_internal v_ptFacette in v_normalisationDuSens)
                    {
                        v_listeIndices.Add(v_indiceParIdPoint[v_ptFacette.p00_id]);
                    }
                    p01_listeIndexPointsfacettes.Add(v_listeIndices);
                }
            }

            return new Triangulation(p00_geoPoint, p01_listeIndexPointsfacettes.SelectMany(c => c).ToList());


        }
        public static ModelRoot GenerateTIN(HeightMap hMap, double precision, SharpGltfService gltf, PBRTexture textures, int srid)
        {
            return TINGeneration.AddTINMesh(gltf.CreateNewModel(), hMap, precision, gltf, textures, srid);
        }

        public static ModelRoot AddTINMesh(ModelRoot model, HeightMap hMap, double precision, SharpGltfService gltf, PBRTexture textures, int srid)
        {
            Triangulation triangulation = TINGeneration.GenerateTIN(hMap, precision, srid);

            return gltf.AddTerrainMesh(model, triangulation, textures, doubleSided: true);

        }


        private static List<BeanPoint_internal> GetGeoPointsByHMap(HeightMap p_hMap, int p_srid)
        {
            return p_hMap.Coordinates.Select(c => GetPointInternalFromGeoPoint(c, p_srid)).ToList();
        }
        private static BeanPoint_internal GetPointInternalFromGeoPoint(GeoPoint p_geoPoint, int p_srid)
        {
            BeanPoint_internal v_ptInternal = new BeanPoint_internal(p_geoPoint.Longitude, p_geoPoint.Latitude, p_geoPoint.Elevation.GetValueOrDefault(0), p_srid);
            return v_ptInternal;
        }
    }
}
